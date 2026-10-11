using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Studio.Core;

namespace Studio.Raple;

public record ReplayPoint(long Ms, string Signature, int Events, int Shots, int PlayerShots, int Movements = 0);
public record ReplayCommand(long Ms, string Kind, int DurationMs = 0, EncounterInput? Input = null, double Fraction = 0, ReplayPoint? Result = null);
public record ReplayIdentity(string SourceHash, string NativeSourcesHash, string ResourceHash, string RuntimeHash, string CompiledBuildId);

// Witnesses cover observable native state and cumulative event/emission order.
// Their digest is independent of how a recorded advance is split during seeking.
public sealed class ReplayJournal
{
    readonly object gate = new();
    readonly List<ReplayCommand> commands = [];
    string eventsHash = "", shotsHash = "", playerShotsHash = "", movementHash = "";
    int events, shots, playerShots, movements;
    public ReplayPoint Initial { get; }
    public ReplayJournal(IEncounterSession session)
    {
        if (session.Ms != 0) throw new ArgumentException("Recording must start at encounter initialization.");
        Initial = Observe(session);
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    ReplayPoint Observe(IEncounterSession session)
    {
        void Append<T>(IReadOnlyList<T> values, ref int cursor, ref string hash)
        { while (cursor < values.Count) hash = Hash(hash + JsonSerializer.Serialize(values[cursor++])); }
        Append(session.Events, ref events, ref eventsHash); Append(session.Shots, ref shots, ref shotsHash); Append(session.PlayerShots, ref playerShots, ref playerShotsHash);
        Append(session.Movements,ref movements,ref movementHash);
        var signature = Hash(JsonSerializer.Serialize(new {
            session.Ms, session.PlayerHp, session.PlayerX, session.PlayerY, session.BossHp, session.BossMaxHp, session.Seed,
            session.PlayerFacing, session.PlayerAttackAngle, session.PlayerMoveSpeed, session.PlayerAttackStart, session.PlayerAttackPeriod,
            session.PlayerStats, session.Coverage, actors = session.Entities.OrderBy(e => e.Id), projectiles = session.Projectiles,
            timers = session.InspectTimers(), eventsHash, shotsHash, playerShotsHash, movementHash
        }));
        return new(session.Ms, signature, events, shots, playerShots,movements);
    }
    public ReplayPoint ObserveCurrent(IEncounterSession session) { lock (gate) return Observe(session); }
    internal void Record(IEncounterSession session, ReplayCommand command)
    { lock (gate) commands.Add(command with { Result = Observe(session) }); }
    public ReplayCommand[] Commands { get { lock (gate) return commands.ToArray(); } }
    public int CommandCount { get { lock(gate) return commands.Count; } }
}

public sealed record EncounterReplay(int Version, StudioProject Project, string? Phase, ReplayIdentity Identity,
    ReplayPoint Initial, ReplayCommand[] Commands, EncounterEvent[] Events, Emission[] Shots, Emission[] PlayerShots,MovementSample[] Movements)
{
    public long DurationMs => Commands.LastOrDefault()?.Result?.Ms ?? 0;
    public static ReplayIdentity Identify(RapleAdapter adapter, StudioProject project, string compiledBuildId = "")
    {
        var assemblies = new[] { typeof(EncounterSession).Assembly, typeof(BossPatternSim.SimEngine).Assembly,
            typeof(TKR.WorldServer.logic.State).Assembly, typeof(TKR.Shared.resources.Resources).Assembly };
        var runtime = string.Join("\n", assemblies.Distinct().OrderBy(a => a.GetName().Name).Select(a => a.GetName().Name + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))));
        return new(ReplayJournal.Hash(JsonSerializer.Serialize(project)), NativeInputFingerprint(adapter.ServerRoot),
            SourceVerification.Fingerprint(Path.Combine(adapter.ServerRoot, "TKR.Shared/resources"), "*"),
            ReplayJournal.Hash(runtime + "\n" + Environment.Version), compiledBuildId);
    }
    public static string NativeInputFingerprint(string serverRoot)
    {
        var directory=Path.Combine(serverRoot,"TKR.WorldServer"); var projectPath=Path.Combine(directory,"TKR.WorldServer.csproj");
        var embedded=System.Xml.Linq.XDocument.Load(projectPath).Descendants("EmbeddedResource").Where(e=>e.Attribute("Include")!=null)
            .Select(e=>Path.GetFullPath(Path.Combine(directory,(string)e.Attribute("Include")!)));
        var files=SourceVerification.SourcePaths(serverRoot).Append(projectPath).Concat(embedded).Distinct().OrderBy(p=>p,StringComparer.Ordinal);
        return ReplayJournal.Hash(string.Join("\n",files.Select(p=>Path.GetRelativePath(serverRoot,p).Replace('\\','/')+"\n"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))));
    }
    public static EncounterReplay Capture(StudioProject project, string? phase, ReplayIdentity identity, IEncounterSession session)
    {
        var recording = session.Recording ?? throw new InvalidOperationException("This encounter was not recorded.");
        var commands = recording.Commands; var point = commands.LastOrDefault()?.Result ?? recording.Initial;
        var replay = new EncounterReplay(1, project, phase, identity, recording.Initial, commands, session.Events.Take(point.Events).ToArray(),session.Shots.Take(point.Shots).ToArray(),session.PlayerShots.Take(point.PlayerShots).ToArray(),session.Movements.Take(point.Movements).ToArray());
        replay.Validate(); return replay;
    }
    public void Validate()
    {
        static bool Hash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
        if (Version != 1 || Project == null || Identity == null || Initial == null || Commands == null || Events == null || Shots == null || PlayerShots == null || Movements==null || Movements.Length>500_000 ||
            Project.Version != 1 || Project.Player == null || Project.Identities == null || Project.LockedNodes == null || Project.CollapsedNodes == null || Project.Layout == null ||
            string.IsNullOrEmpty(Project.SourceFile) || string.IsNullOrEmpty(Project.EntityId) || Project.Source == null || Project.Source.Length > 4_000_000 ||
            Commands.Length > 100_000 || Events.Length > 30_000 || Shots.Length > 200_000 || PlayerShots.Length > 200_000 ||
            !Hash(Identity.SourceHash) || !Hash(Identity.NativeSourcesHash) || !Hash(Identity.ResourceHash) || !Hash(Identity.RuntimeHash) ||
            Identity.CompiledBuildId != "" && !Hash(Identity.CompiledBuildId) || Identity.SourceHash != ReplayJournal.Hash(JsonSerializer.Serialize(Project)))
            throw new ArgumentException("Unsupported, modified or oversized encounter recording.");
        long ms = 0; var previous = Initial;
        if (Initial.Ms != 0 || !Hash(Initial.Signature) || Initial.Events < 0 || Initial.Shots < 0 || Initial.PlayerShots < 0 || Initial.Movements<0) throw new ArgumentException("Invalid initial replay witness.");
        foreach (var command in Commands)
        {
            if (command == null || command.Ms != ms || command.Result == null || !Hash(command.Result.Signature) ||
                command.Kind is not ("advance" or "boss-hp" or "remove-minions")) throw new ArgumentException("Invalid replay command order.");
            if (command.Kind == "advance")
            {
                var input = command.Input;
                if (command.DurationMs is < 5 or > 1000 || command.DurationMs % EncounterSession.SampleMs != 0 || input == null ||
                    !double.IsFinite(input.X) || !double.IsFinite(input.Y) || !double.IsFinite(input.AimX) || !double.IsFinite(input.AimY))
                    throw new ArgumentException("Invalid recorded input.");
                ms += command.DurationMs;
            }
            else if (command.DurationMs != 0 || command.Kind == "boss-hp" && (!double.IsFinite(command.Fraction) || command.Fraction < 0 || command.Fraction > 1)) throw new ArgumentException("Invalid recorded test override.");
            if (ms > 300_000 || command.Result.Ms != ms || command.Result.Events < previous.Events || command.Result.Shots < previous.Shots || command.Result.PlayerShots < previous.PlayerShots || command.Result.Movements<previous.Movements) throw new ArgumentException("Invalid replay witness.");
            previous = command.Result;
        }
        if (Events.Any(e => e == null || e.Ms < 0 || e.Ms > ms) || previous.Events != Events.Length || previous.Shots != Shots.Length || previous.PlayerShots != PlayerShots.Length || previous.Movements!=Movements.Length ||
            Movements.Any(m=>m==null || m.Ms<0 || m.Ms>ms || m.DurationMs is not (5 or 100) || !float.IsFinite(m.FromX) || !float.IsFinite(m.FromY) || !float.IsFinite(m.ToX) || !float.IsFinite(m.ToY)) ||
            Shots.Concat(PlayerShots).Any(s=>s==null || s.Ms<0 || s.Ms>ms || !float.IsFinite(s.X) || !float.IsFinite(s.Y) || !float.IsFinite(s.Angle) || !float.IsFinite(s.AngleIncrement)))
            throw new ArgumentException("Recorded events and emissions do not match their witnesses.");
    }
    public void Save(string path)
    {
        Validate(); var json = JsonSerializer.Serialize(this, BehaviorDocument.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > 64_000_000) throw new ArgumentException("Recording exceeds the 64 MB file limit.");
        var target = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static EncounterReplay Load(string path)
    {
        if (new FileInfo(path).Length > 64_000_000) throw new ArgumentException("Recording exceeds the 64 MB file limit.");
        var replay = JsonSerializer.Deserialize<EncounterReplay>(File.ReadAllText(path), BehaviorDocument.JsonOptions) ?? throw new ArgumentException("Empty recording.");
        replay.Validate(); return replay;
    }
}

public sealed class ReplayCursor
{
    readonly EncounterReplay replay;
    int commandIndex;
    public IEncounterSession Session { get; }
    ReplayCursor(EncounterReplay replay, IEncounterSession session) { this.replay = replay; Session = session; }
    public static async Task<ReplayCursor> RestoreAsync(RapleAdapter adapter, EncounterReplay replay, long targetMs,
        CompiledRuntime.PreparedRuntime? prepared = null, IProgress<long>? progress = null, CancellationToken cancellation = default)
    {
        replay.Validate(); ValidateTime(replay, targetMs); cancellation.ThrowIfCancellationRequested();
        var identity = await Task.Run(() => EncounterReplay.Identify(adapter, replay.Project, replay.Identity.CompiledBuildId), cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (identity != replay.Identity) throw new InvalidOperationException("Recording source, resources or runtime changed. Record a fresh run.");
        IEncounterSession? session = null;
        try
        {
            if (replay.Identity.CompiledBuildId != "")
            {
                prepared ??= await CompiledRuntime.PrepareAsync(adapter.ServerRoot, replay.Project, cancellation: cancellation);
                if (prepared.Stamp.BuildId != replay.Identity.CompiledBuildId) throw new InvalidOperationException("Recording compiled build changed. Record a fresh run.");
                session = await prepared.StartAsync(phase: replay.Phase, cancellation: cancellation);
            }
            else
            {
                if (identity.ResourceHash != adapter.LoadedResourceHash) throw new InvalidOperationException("Loaded resources changed. Restart Studio before replay.");
                var document = new BehaviorDocument(replay.Project.Source, adapter, replay.Project.Identities, replay.Project.SourceFile);
                if (adapter.NeedsCompiledRuntime(document, replay.Project.EntityId)) throw new InvalidOperationException("Recording runtime mode does not match its source.");
                session = new EncounterSession(adapter, document, replay.Project.EntityId, replay.Project.Seed, replay.Project.Player, replay.Project.ArenaSize, replay.Phase);
            }
            session.BreakpointKind = null; session.Recording = new(session);
            Match(session.Recording.Initial, replay.Initial);
            var cursor = new ReplayCursor(replay, session);
            await cursor.AdvanceToAsync(targetMs, progress, cancellation);
            return cursor;
        }
        catch { session?.Dispose(); throw; }
    }
    static void ValidateTime(EncounterReplay replay, long ms)
    { if (ms < 0 || ms > replay.DurationMs || ms % EncounterSession.SampleMs != 0) throw new ArgumentOutOfRangeException(nameof(ms), "Seek within the recorded run in 5 ms samples."); }
    static void Match(ReplayPoint actual, ReplayPoint expected)
    { if (actual != expected) throw new InvalidOperationException("Native replay diverged at " + expected.Ms + " ms. The recording cannot certify this run."); }
    public async Task AdvanceToAsync(long targetMs, IProgress<long>? progress = null, CancellationToken cancellation = default)
    {
        ValidateTime(replay, targetMs);
        if (targetMs < Session.Ms) throw new ArgumentException("Restart replay before seeking backwards.");
        Session.BreakpointKind = null;
        var yield = System.Threading.SynchronizationContext.Current != null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (commandIndex < replay.Commands.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var command = replay.Commands[commandIndex]; if (command.Ms > targetMs) break;
            if (command.Kind == "advance")
            {
                var end = command.Ms + command.DurationMs;
                if (Session.Ms < end)
                {
                    var duration = (int)(Math.Min(targetMs, end) - Session.Ms); if (duration <= 0) break;
                    await Session.AdvanceAsync(command.Input!, duration, cancellation);
                    if (Session.Ms < end) break;
                }
            }
            else if (Session is CompiledRuntime compiled)
            {
                if (command.Kind == "boss-hp") await compiled.SetBossHpAsync(command.Fraction, cancellation);
                else await compiled.RemoveMinionsAsync(cancellation);
            }
            else if (command.Kind == "boss-hp") Session.SetBossHpFraction(command.Fraction);
            else Session.RemoveMinionsForTest();
            Match(Session.Recording!.ObserveCurrent(Session), command.Result!); commandIndex++;
            progress?.Report(Session.Ms);
            if (yield && watch.ElapsedMilliseconds >= 12) { await Task.Yield(); watch.Restart(); }
        }
        cancellation.ThrowIfCancellationRequested();
        var completed=commandIndex==0?replay.Initial:replay.Commands[commandIndex-1].Result!;
        if(commandIndex<replay.Commands.Length && replay.Commands[commandIndex].Kind=="advance" && Session.Ms>replay.Commands[commandIndex].Ms)
        {
            var next=replay.Commands[commandIndex].Result!;
            static IEnumerable<T> Prefix<T>(IEnumerable<T> values,int before,int after,long ms,Func<T,long> time)
                =>values.Take(after).Where((value,index)=>index<before || time(value)<ms);
            if(!Session.Events.SequenceEqual(Prefix(replay.Events,completed.Events,next.Events,Session.Ms,e=>e.Ms)) ||
               !Session.Shots.SequenceEqual(Prefix(replay.Shots,completed.Shots,next.Shots,Session.Ms,s=>s.Ms)) ||
               !Session.PlayerShots.SequenceEqual(Prefix(replay.PlayerShots,completed.PlayerShots,next.PlayerShots,Session.Ms,s=>s.Ms)) ||
               !Session.Movements.SequenceEqual(Prefix(replay.Movements,completed.Movements,next.Movements,Session.Ms,m=>m.Ms)))
                throw new InvalidOperationException("Native replay event/emission prefix diverged at "+Session.Ms+" ms.");
        }
        progress?.Report(Session.Ms);
    }
}
