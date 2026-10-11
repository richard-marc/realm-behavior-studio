using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Studio.Core;

namespace Studio.Raple;

public sealed class CompiledRuntime : IEncounterSession, IAsyncDisposable
{
    static readonly ConcurrentDictionary<string, string> bundles = new();
    readonly Process process;
    readonly RuntimeLimits limits;
    readonly SemaphoreSlim commands = new(1);
    readonly StringBuilder log = new();
    readonly Task stderr;
    long sequence;
    bool disposed;
    volatile RuntimeFrame currentFrame = null!;
    public RuntimeFrame Frame => currentFrame;
    public NativeRuntimeStamp Stamp { get; }
    public StudioProject Project { get; }
    public int ProcessId => process.Id;
    public bool IsStopped => disposed || process.HasExited;
    public string ArtifactDirectory => process.StartInfo.WorkingDirectory;
    volatile ImmutableList<EncounterEvent> events = ImmutableList<EncounterEvent>.Empty;
    volatile ImmutableList<Emission> shots = ImmutableList<Emission>.Empty, playerShots = ImmutableList<Emission>.Empty;
    volatile ImmutableList<ReplayInput> inputs = ImmutableList<ReplayInput>.Empty;
    public IReadOnlyList<EncounterEvent> Events => events;
    public IReadOnlyList<Emission> Shots => shots;
    public IReadOnlyList<Emission> PlayerShots => playerShots;
    public IReadOnlyList<ReplayInput> Inputs => inputs;
    public ReplayJournal? Recording { get; set; }
    public PreparedRuntime? Prepared { get; private set; }
    volatile ImmutableList<MovementSample> movements = ImmutableList<MovementSample>.Empty;
    public IReadOnlyList<MovementSample> Movements => movements;
    volatile ArenaSceneView arena = null!;
    public ArenaSceneView Arena=>arena;
    public long Ms => Frame.Ms;
    public int PlayerHp => Frame.PlayerHp;
    public bool Dead => PlayerHp <= 0;
    public float PlayerX => Frame.PlayerX;
    public float PlayerY => Frame.PlayerY;
    public int BossHp => Frame.BossHp;
    public int BossMaxHp => Frame.BossMaxHp;
    public int Seed => Frame.Seed;
    public double StartMs => Frame.StartMs;
    public bool PauseRequested => Frame.PauseRequested;
    public string? BreakpointKind { get; set; }
    public string PlayerClass => Frame.PlayerClass;
    public WeaponEntry Weapon => Frame.Weapon;
    public double PlayerFacing => Frame.PlayerFacing;
    public double PlayerAttackAngle => Frame.PlayerAttackAngle;
    public double PlayerMoveSpeed => Frame.PlayerMoveSpeed;
    public long PlayerAttackStart => Frame.PlayerAttackStart;
    public double PlayerAttackPeriod => Frame.PlayerAttackPeriod;
    public IReadOnlyList<int> PlayerStats => Frame.PlayerStats;
    public string Coverage => "Compiled original encounter · " + Frame.Coverage;
    public IReadOnlyList<EntityView> Entities => Frame.Entities;
    public IReadOnlyList<BulletView> Projectiles => Frame.Projectiles;
    public void SetBossHpFraction(double fraction) => SetBossHpAsync(fraction).GetAwaiter().GetResult();
    public void RemoveMinionsForTest() => RemoveMinionsAsync().GetAwaiter().GetResult();
    public void Advance(EncounterInput input, int durationMs) => AdvanceAsync(input, durationMs).GetAwaiter().GetResult();
    public string InspectTimers() => Frame.Timers;
    public string Diagnostics { get { lock (log) return log.ToString(); } }
    CompiledRuntime(Process process, NativeRuntimeStamp stamp, RuntimeLimits limits, StudioProject project)
    {
        this.process = process; Stamp = stamp; this.limits = limits; Project = project;
        stderr = DrainLog();
    }
    public static async Task<CompiledRuntime> StartAsync(string serverRoot, StudioProject project, string? phase = null,
        string? hostDirectory = null, string? artifactRoot = null, RuntimeLimits? limits = null, CancellationToken cancellation = default)
    {
        var prepared = await PrepareAsync(serverRoot, project, hostDirectory, artifactRoot, limits, cancellation).ConfigureAwait(false);
        return await prepared.StartAsync(phase: phase, cancellation: cancellation).ConfigureAwait(false);
    }
    public static async Task<PreparedRuntime> PrepareAsync(string serverRoot, StudioProject project,
        string? hostDirectory = null, string? artifactRoot = null, RuntimeLimits? limits = null, CancellationToken cancellation = default)
    {
        limits ??= new();
        if (limits.StartupSeconds is < 1 or > 120 || limits.CommandSeconds is < 1 or > 30 || limits.MemoryBytes < 64 * 1024 * 1024) throw new ArgumentException("Invalid runtime limits.");
        hostDirectory ??= Path.Combine(AppContext.BaseDirectory, "runtime-host");
        if (!File.Exists(Path.Combine(hostDirectory, "Studio.RuntimeHost.dll"))) throw new FileNotFoundException("The compiled runtime host is missing. Rebuild Studio with its runtime host.");
        artifactRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "Runtime");
        var compilation = await Task.Run(() => SourceVerification.Compile(serverRoot, project.SourceFile, project.Source, cancellation), cancellation).ConfigureAwait(false);
        if (!compilation.Verification.Accepted) throw new ArgumentException("Original compiler rejected the runtime:\n" + string.Join("\n", compilation.Verification.Errors.Take(8)));
        var prepared = await Task.Run(() =>
        {
            var adapter = new RapleAdapter(serverRoot);
            var schema = new SourceBehaviorSchema(adapter, compilation.Compiler!, project.SourceFile);
            var document = new BehaviorDocument(project.Source, schema, project.Identities);
            var bindings = document.AllNodes.Select(n => (Node: n, Description: document.SchemaAt(n)?.DescribeExpression(document.Expression(n))))
                .Where(p => p.Description != null).Select(p => new RuntimeSourceBinding(p.Node.Start, document.Expression(p.Node), p.Description!)).DistinctBy(b => (b.Start, b.Expression)).ToArray();
            return (Project: project with { Identities = document.Identities }, Bindings: bindings,Registrations:document.EncounterSources.ToArray(),Definitions:document.Definitions.Select(d=>d.Definition!).ToArray());
        }, cancellation).ConfigureAwait(false);
        project = prepared.Project;
        cancellation.ThrowIfCancellationRequested();
        var hostFiles = Directory.EnumerateFiles(hostDirectory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (Name: Path.GetRelativePath(hostDirectory, p), Data: File.ReadAllBytes(p))).ToArray();
        var hostHash = SourceVerification.Hash(hostFiles);
        var resourceHash = await Task.Run(() => SourceVerification.Fingerprint(Path.Combine(serverRoot, "TKR.Shared/resources"), "*"), cancellation).ConfigureAwait(false);
        var assemblyHash = SourceVerification.Hash([("TKR.WorldServer.dll", compilation.Assembly)]);
        var symbolsHash = SourceVerification.Hash([("TKR.WorldServer.pdb", compilation.Symbols)]);
        var buildId = SourceVerification.Hash([("source", JsonSerializer.SerializeToUtf8Bytes(compilation.Stamp)), ("host", Encoding.UTF8.GetBytes(hostHash)), ("resources", Encoding.UTF8.GetBytes(resourceHash)), ("assembly", compilation.Assembly), ("runtime", Encoding.UTF8.GetBytes(Environment.Version.ToString()))]);
        var stamp = new NativeRuntimeStamp(1, buildId, compilation.Stamp, hostHash, resourceHash, assemblyHash, symbolsHash, Environment.Version.ToString());
        // Each process receives an immutable private bundle. Never overwrite a loaded
        // assembly, the desktop's dependencies or the user's server installation.
        var files = hostFiles.ToDictionary(f => f.Name, f => f.Data, StringComparer.OrdinalIgnoreCase);
        files["TKR.WorldServer.dll"] = compilation.Assembly; files["TKR.WorldServer.pdb"] = compilation.Symbols;
        var cacheKey = Path.GetFullPath(artifactRoot) + "|" + buildId;
        var directory = bundles.TryGetValue(cacheKey, out var cached) && files.All(f => File.Exists(Path.Combine(cached, f.Key)) && File.ReadAllBytes(Path.Combine(cached, f.Key)).AsSpan().SequenceEqual(f.Value))
            ? cached : Path.Combine(artifactRoot, buildId[..16] + "-" + Guid.NewGuid().ToString("N"));
        cancellation.ThrowIfCancellationRequested();
        if (directory != cached)
        {
            Directory.CreateDirectory(directory);
            foreach (var file in files) { cancellation.ThrowIfCancellationRequested(); var target = Path.Combine(directory, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, file.Value); }
            File.WriteAllText(Path.Combine(directory, "runtime-stamp.json"), JsonSerializer.Serialize(stamp, BehaviorDocument.JsonOptions));
            bundles[cacheKey] = directory;
        }
        return new PreparedRuntime(Path.GetFullPath(serverRoot), directory, project, prepared.Bindings,prepared.Registrations,prepared.Definitions, stamp, limits, files);
    }
    public sealed class PreparedRuntime
    {
        readonly string serverRoot, directory;
        readonly StudioProject project;
        readonly RuntimeSourceBinding[] bindings;
        readonly EncounterSource[] registrations;
        readonly SourceDefinition[] definitions;
        readonly RuntimeLimits limits;
        readonly IReadOnlyDictionary<string, byte[]> files;
        public NativeRuntimeStamp Stamp { get; }
        internal PreparedRuntime(string serverRoot, string directory, StudioProject project, RuntimeSourceBinding[] bindings,EncounterSource[] registrations,SourceDefinition[] definitions, NativeRuntimeStamp stamp, RuntimeLimits limits, IReadOnlyDictionary<string, byte[]> files)
        { this.serverRoot = serverRoot; this.directory = directory; this.project = project; this.bindings = bindings;this.registrations=registrations;this.definitions=definitions; Stamp = stamp; this.limits = limits; this.files = files; }
        // Reuse compilation, never executing native objects or RNG state. Every
        // scenario gets a fresh process and the same verified immutable bundle.
        public async Task<CompiledRuntime> StartAsync(PlayerPreset? player = null, string? phase = null, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                foreach (var (name, bytes) in files)
                {
                    cancellation.ThrowIfCancellationRequested(); var path = Path.Combine(directory, name);
                    if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("Prepared runtime files changed. Prepare the encounter again.");
                }
            }, cancellation).ConfigureAwait(false);
            var scenario = project with { Player = player ?? project.Player };
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(directory, "Studio.RuntimeHost.dll"));
            start.ArgumentList.Add("--parent"); start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            cancellation.ThrowIfCancellationRequested();
            var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the compiled runtime.");
            var runtime = new CompiledRuntime(process, Stamp, limits, scenario);
            runtime.Prepared = this;
            try { await runtime.Exchange(new(RuntimeProtocol.Version, 0, "start", new(serverRoot, scenario, Stamp.ResourceHash, phase, bindings,registrations,definitions)), limits.StartupSeconds, cancellation).ConfigureAwait(false); return runtime; }
            catch { await runtime.DisposeAsync().ConfigureAwait(false); throw; }
        }
    }
    async Task DrainLog()
    {
        var buffer = new char[2048];
        try { int count; while ((count = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) > 0) { lock (log) { log.Append(buffer, 0, count); if (log.Length > 16000) log.Remove(0, log.Length - 16000); } } }
        catch (IOException) { }
    }
    public Task AdvanceAsync(EncounterInput input, int durationMs = 100, CancellationToken cancellation = default)
        => Exchange(new(RuntimeProtocol.Version, 0, "advance", Input: input, DurationMs: durationMs, Breakpoint: BreakpointKind), limits.CommandSeconds, cancellation);
    public async Task<CombatSample> SampleCombatAsync(CombatPreviewOptions options, bool density = true, CancellationToken cancellation = default)
    {
        options.Validate();
        return await Exchange(new(RuntimeProtocol.Version, 0, "sample-combat", Preview: options, Density: density), limits.CommandSeconds, cancellation).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The runtime did not return combat samples.");
    }
    public Task SetBossHpAsync(double fraction, CancellationToken cancellation = default)
        => Exchange(new(RuntimeProtocol.Version, 0, "boss-hp", Fraction: fraction), limits.CommandSeconds, cancellation);
    public Task RemoveMinionsAsync(CancellationToken cancellation = default)
        => Exchange(new(RuntimeProtocol.Version, 0, "remove-minions"), limits.CommandSeconds, cancellation);
    public Task SetBreakpointAsync(string? kind, CancellationToken cancellation = default)
    { BreakpointKind = kind; return Exchange(new(RuntimeProtocol.Version, 0, "breakpoint", Breakpoint: kind), limits.CommandSeconds, cancellation); }
    async Task<CombatSample?> Exchange(RuntimeRequest request, int seconds, CancellationToken cancellation)
    {
        await commands.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (process.HasExited) throw new InvalidOperationException("Compiled runtime has stopped. Start a fresh session.");
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bound.CancelAfter(TimeSpan.FromSeconds(seconds));
            request = request with { Sequence = ++sequence };
            var payload = JsonSerializer.Serialize(request);
            if (payload.Length > RuntimeProtocol.MaxLineLength) throw new ArgumentException("Runtime request exceeds the protocol limit.");
            await process.StandardInput.WriteLineAsync(payload.AsMemory(), bound.Token).ConfigureAwait(false); await process.StandardInput.FlushAsync(bound.Token).ConfigureAwait(false);
            var read = ReadBoundedLine(bound.Token);
            while (!read.IsCompleted)
            {
                await Task.WhenAny(read, Task.Delay(50, bound.Token)).ConfigureAwait(false); bound.Token.ThrowIfCancellationRequested();
                CheckMemory();
            }
            var response = JsonSerializer.Deserialize<RuntimeResponse>(await read.ConfigureAwait(false)) ?? throw new InvalidOperationException("Empty runtime response.");
            if (response.Version != RuntimeProtocol.Version || response.Sequence != request.Sequence) throw new InvalidOperationException("Runtime response version or sequence mismatch.");
            if (response.Error != null) throw new InvalidOperationException("Compiled runtime: " + response.Error);
            var frame = response.Frame ?? throw new InvalidOperationException("Runtime response is missing its frame.");
            if (frame.LoadedAssemblyHash != Stamp.AssemblyHash || frame.RuntimeVersion != Stamp.RuntimeVersion) throw new InvalidOperationException("Compiled runtime provenance does not match its build.");
            CheckMemory(); // Fast responses must observe the budget too.
            if (request.Command == "advance") inputs = inputs.Add(new(Frame.Ms, request.Input!, request.DurationMs));
            if (request.Command == "sample-combat")
            {
                var sample = response.Preview ?? throw new InvalidOperationException("The runtime omitted its combat preview result.");
                if (sample.DensitySampled != request.Density || sample.PeakIncoming < 0 || !double.IsFinite(sample.MeanIncoming) || sample.MeanIncoming < 0) throw new InvalidOperationException("Invalid combat density response.");
                if (sample.ActualMs < 0 || sample.ActualMs > request.Preview!.DurationMs || sample.ActualMs % EncounterSession.SampleMs != 0 || frame.Ms != Frame.Ms + sample.ActualMs || sample.ExposureSamples.Length != request.Preview.Columns * request.Preview.Columns || sample.ExposureSamples.Any(n => n < 0)) throw new InvalidOperationException("Invalid combat sample response.");
                inputs = inputs.AddRange(Enumerable.Range(0, sample.ActualMs / EncounterSession.SampleMs).Select(i => new ReplayInput(Frame.Ms + i * EncounterSession.SampleMs, new(), EncounterSession.SampleMs)));
            }
            var previousMs = currentFrame?.Ms ?? 0;
            if(frame.ArenaDelta is {} map)arena=map;
            else if(arena==null)throw new InvalidOperationException("Runtime omitted its initial native arena snapshot.");
            movements=movements.AddRange(frame.MovementDelta ?? throw new InvalidOperationException("Runtime omitted native movement samples."));
            events = events.AddRange(frame.Events); shots = shots.AddRange(frame.Shots); playerShots = playerShots.AddRange(frame.PlayerShots); currentFrame = frame;
            if (request.Command is "advance" or "boss-hp" or "remove-minions") Recording?.Record(this, new(previousMs, request.Command, request.Command == "advance" ? request.DurationMs : 0, request.Input, request.Fraction));
            else if (request.Command == "sample-combat" && Recording != null) Recording = null;
            return response.Preview;
        }
        catch (OperationCanceledException) { Kill(); if (cancellation.IsCancellationRequested) throw; throw new TimeoutException("Compiled runtime exceeded its time limit and was stopped."); }
        catch { Kill(); throw; }
        finally { commands.Release(); }
    }
    void CheckMemory()
    {
        try { process.Refresh(); if (!process.HasExited && process.WorkingSet64 > limits.MemoryBytes) throw new InvalidOperationException("Compiled runtime exceeded its memory limit."); }
        catch (InvalidOperationException) when (disposed || process.HasExited) { }
    }
    async Task<string> ReadBoundedLine(CancellationToken cancellation)
    {
        var line = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            line.Append(buffer, 0, newline < 0 ? count : newline);
            if (line.Length > RuntimeProtocol.MaxLineLength) throw new InvalidOperationException("Runtime response exceeds the protocol limit.");
            if (newline >= 0)
            {
                if (newline != count - 1) throw new InvalidOperationException("Unexpected extra runtime response data.");
                return line.ToString().TrimEnd('\r');
            }
        }
        throw new InvalidOperationException("Compiled runtime exited without a response. " + Diagnostics);
    }
    void Kill() { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; Kill(); await process.WaitForExitAsync().ConfigureAwait(false); await stderr.ConfigureAwait(false); process.Dispose();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public void ExportTrace(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new { runtime = Stamp, seed = Frame.Seed,
        entity = Project.EntityId, player = Project.Player, tickMs = 100, sampleMs = EncounterSession.SampleMs, durationMs = Frame.Ms, coverage = Frame.Coverage, actors = Entities, events = Events, shots = Shots, playerShots = PlayerShots, inputs = Inputs }, BehaviorDocument.JsonOptions));
}
