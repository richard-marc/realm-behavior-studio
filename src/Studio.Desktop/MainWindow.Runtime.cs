using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task TryRuntime(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Try(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()); }
    }
    async Task<bool> StartSessionAsync(string? phase = null)
    {
        ClearViewportEditors();
        session = null; running = false; Viewport.Session = null;
        RefreshRuntimeActors();
        if (!adapter.NeedsCompiledRuntime(document, selectedEntity!.Id)) { StartSession(phase); return true; }
        var project = Snapshot(); var expectedDocument = document; var revision = document.Revision;
        using var cancellation = new CancellationTokenSource(); runtimeStarting = cancellation;
        Status.Text = "Preparing encounter…"; UpdatePlayButton();
        try
        {
            var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, project, phase, cancellation: cancellation.Token);
            if (cancellation.IsCancellationRequested || document != expectedDocument || document.Revision != revision || selectedEntity?.Id != project.EntityId)
            { await native.DisposeAsync(); return false; }
            runtimeStarting = null; session = native; BindSession(phase);
            Coverage.Text = "Compiled encounter · seed " + native.Seed;
            return true;
        }
        finally { if (ReferenceEquals(runtimeStarting, cancellation)) { runtimeStarting = null; UpdatePlayButton(); } }
    }
}
