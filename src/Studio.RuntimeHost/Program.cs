using System.Text.Json;
using System.Diagnostics;
using Studio.Core;
using Studio.Raple;

// Keep native initialization, RNG and every tick on this one thread. Stdout is
// reserved for the versioned protocol; native console output belongs to stderr.
var protocolOutput = Console.Out;
Console.SetOut(Console.Error);
if (args.Length != 2 || args[0] != "--parent" || !int.TryParse(args[1], out var parentId)) return 2;
try
{
    var parent = Process.GetProcessById(parentId);
    new Thread(() => { using (parent) parent.WaitForExit(); Environment.Exit(0); }) { IsBackground = true, Name = "Studio parent lifetime" }.Start();
}
catch (ArgumentException) { return 0; }
EncounterSession? session = null;
int events = 0, shots = 0, playerShots = 0, movements = 0;
long arenaRevision=-1;
long lastSequence = 0;
while (Console.ReadLine() is { } line)
{
    RuntimeRequest? request = null;
    try
    {
        CombatSample? preview = null;
        if (line.Length > RuntimeProtocol.MaxLineLength) throw new ArgumentException("Runtime request exceeds the protocol limit.");
        request = JsonSerializer.Deserialize<RuntimeRequest>(line) ?? throw new ArgumentException("Empty runtime request.");
        if (request.Version != RuntimeProtocol.Version || request.Sequence != lastSequence + 1) throw new ArgumentException("Runtime protocol version or sequence mismatch.");
        lastSequence = request.Sequence;
        if (request.Command == "start")
        {
            if (session != null) throw new InvalidOperationException("Restart uses a fresh runtime process.");
            var start = request.Start ?? throw new ArgumentException("Missing runtime initialization.");
            var resourceRoot = Path.Combine(start.ServerRoot, "TKR.Shared/resources");
            if (SourceVerification.Fingerprint(resourceRoot, "*") != start.ResourceHash) throw new InvalidOperationException("Game resources changed before runtime initialization. Restart with the current resources.");
            var adapter = new RapleAdapter(start.ServerRoot);
            var project = start.Project;
            if (project.Version != 1) throw new ArgumentException("Unsupported project version.");
            var document = new BehaviorDocument(project.Source, new PreparedBehaviorSchema(adapter, start.SourceBindings ?? [],start.Registrations), project.Identities);
            session = new(adapter, document, project.EntityId, project.Seed, project.Player, project.ArenaSize, start.Phase, originalRuntimeReference: true,arena:project.Arena);
            if (SourceVerification.Fingerprint(resourceRoot, "*") != start.ResourceHash) throw new InvalidOperationException("Game resources changed during runtime initialization. Restart with the current resources.");
        }
        else
        {
            if (session == null) throw new InvalidOperationException("Initialize the runtime first.");
            switch (request.Command)
            {
                case "advance": session.BreakpointKind = request.Breakpoint; session.Advance(request.Input ?? new(), request.DurationMs); break;
                case "boss-hp": session.SetBossHpFraction(request.Fraction); break;
                case "remove-minions": session.RemoveMinionsForTest(); break;
                case "breakpoint": session.BreakpointKind = request.Breakpoint; break;
                case "inspect": break;
                case "sample-combat": preview = NativeCombatSampling.Run(session, request.Preview ?? throw new ArgumentException("Missing combat preview options."), request.Density); break;
                default: throw new ArgumentException("Unknown runtime command.");
            }
        }
        var response = new RuntimeResponse(RuntimeProtocol.Version, request.Sequence, RuntimeProtocol.Capture(session!, ref events, ref shots, ref playerShots,ref movements,ref arenaRevision), Preview: preview);
        var json = JsonSerializer.Serialize(response);
        if (json.Length > RuntimeProtocol.MaxLineLength) throw new InvalidOperationException("Runtime response exceeds the protocol limit.");
        protocolOutput.WriteLine(json); protocolOutput.Flush();
    }
    catch (Exception error)
    {
        protocolOutput.WriteLine(JsonSerializer.Serialize(new RuntimeResponse(RuntimeProtocol.Version, request?.Sequence ?? 0, Error: error.GetBaseException().Message)));
        protocolOutput.Flush();
        // A failed tick may have partially changed native state. It cannot be resumed.
        return 1;
    }
}
return 0;
