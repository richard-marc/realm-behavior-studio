using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Studio.AI;

public sealed class CodexConversation : IAsyncDisposable
{
    Process? process;
    readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    readonly SemaphoreSlim writing = new(1);
    readonly CancellationTokenSource lifetime = new();
    long nextId;
    string? threadId, turnId;
    TaskCompletionSource<string>? completion;
    public event Action<string>? Delta;
    public event Action<string>? Status;
    public Func<string, JsonElement, Task<object>>? ToolHandler { get; set; }
    public static string[] ProviderArguments => ["app-server", "--listen", "stdio://",
        "-c", "model_provider=\"openai_chatgpt_plan\"",
        "-c", "model_providers.openai_chatgpt_plan.name=\"ChatGPT plan\"",
        "-c", "model_providers.openai_chatgpt_plan.base_url=\"https://api.openai.com/v1\"",
        "-c", "model_providers.openai_chatgpt_plan.env_key=\"ACCESS_TOKEN\"",
        "-c", "model_providers.openai_chatgpt_plan.wire_api=\"responses\"",
        "-c", "model_providers.openai_chatgpt_plan.requires_openai_auth=false",
        "-c", "model_providers.openai_chatgpt_plan.supports_websockets=false",
        "-c", "features.shell_tool=false", "-c", "features.apps=false", "-c", "features.browser_use=false",
        "-c", "features.computer_use=false", "-c", "features.multi_agent=false", "-c", "features.code_mode=false",
        "-c", "web_search=\"disabled\"", "-c", "features.tool_search=false", "-c", "features.tool_search_always_defer_mcp_tools=false"];
    public async Task StartAsync(string executable, AccountProfile profile, string model, object[] dynamicTools, CancellationToken token, string? resumeThread = null)
    {
        if (!profile.PlanEnabled || profile.AccessToken == null) throw new InvalidOperationException("This account has not granted ChatGPT plan usage. Continue with ChatGPT to authorize it.");
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "Codex", Guid.Parse(profile.RegistrationId).ToString("N"));
        var workspace = Path.Combine(home, "workspace"); Directory.CreateDirectory(workspace);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workspace };
        foreach (var arg in ProviderArguments) info.ArgumentList.Add(arg);
        info.Environment["ACCESS_TOKEN"] = profile.AccessToken; info.Environment["CODEX_HOME"] = home;
        info.Environment.Remove("OPENAI_API_KEY"); info.Environment.Remove("OPENAI_BASE_URL");
        process = Process.Start(info) ?? throw new InvalidOperationException("Codex could not start. Choose an installed codex.exe.");
        _ = ReadLoop(); _ = DrainErrors();
        await Request("initialize", new { clientInfo = new { name = ChatGptAccounts.AppName, title = "Realm Behavior Studio", version = "0.1.0" }, capabilities = new { experimentalApi = true } }, token);
        await Write(new { method = "initialized", @params = new { } }, token);
        const string instructions = "You help author Raple Realms behavior documents. Use only realm_studio tools. Unsupported mechanics must remain explicitly unsupported. Never use shell, filesystem tools, connectors, tool_search, or built-in patch tools. Never apply mutations directly: propose a revision-checked structured parameter patch for user preview and atomic Apply. Preserve locked nodes and unrelated behavior. Simulation is a partial source-hosted test proxy; never call it fully verified. Ask a targeted question only when needed. Do not include credentials or unrelated source in context.";
        JsonElement response;
        if (resumeThread != null) response = await Request("thread/resume", new { threadId = resumeThread, model, cwd = workspace, approvalPolicy = "never", sandbox = "read-only", developerInstructions = instructions }, token);
        else response = await Request("thread/start", new { model, cwd = workspace, approvalPolicy = "never", sandbox = "read-only", developerInstructions = instructions, dynamicTools }, token);
        threadId = response.GetProperty("thread").GetProperty("id").GetString(); Status?.Invoke("Connected. Model access is checked by the inference turn, not the catalog.");
    }
    public string? ThreadId => threadId;
    public async Task SendAsync(string text, CancellationToken token)
    {
        if (threadId == null) throw new InvalidOperationException("Connect Codex first.");
        if (completion != null && !completion.Task.IsCompleted) throw new InvalidOperationException("A conversation turn is already running.");
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = await Request("turn/start", new { threadId, input = new[] { new { type = "text", text } } }, token);
        turnId = response.GetProperty("turn").GetProperty("id").GetString();
        var result = await completion.Task.WaitAsync(token);
        if (result != "completed") throw new InvalidOperationException(result == "interrupted" ? "Codex turn was canceled." : "Codex inference failed. Check account permission, model eligibility, usage limits, and connectivity.");
    }
    public async Task CancelAsync(CancellationToken token)
    {
        if (threadId != null && turnId != null && completion?.Task.IsCompleted == false) await Request("turn/interrupt", new { threadId, turnId }, token);
    }
    async Task<JsonElement> Request(string method, object parameters, CancellationToken token)
    {
        var id = Interlocked.Increment(ref nextId); var task = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = task;
        try { await Write(new { id, method, @params = parameters }, token); return await task.Task.WaitAsync(TimeSpan.FromSeconds(45), token); }
        finally { pending.TryRemove(id, out _); }
    }
    async Task Write(object value, CancellationToken token)
    {
        await writing.WaitAsync(token);
        try { await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token); await process.StandardInput.FlushAsync(token); }
        finally { writing.Release(); }
    }
    async Task DrainErrors()
    {
        try { while (await process!.StandardError.ReadLineAsync(lifetime.Token) != null) { /* Never log process diagnostics containing environment credentials. */ } }
        catch (OperationCanceledException) { }
    }
    async Task ReadLoop()
    {
        try
        {
            while (await process!.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                using var json = JsonDocument.Parse(line); var message = json.RootElement;
                if (message.TryGetProperty("method", out var method))
                {
                    var name = method.GetString(); var p = message.TryGetProperty("params", out var parameters) ? parameters : default;
                    if (message.TryGetProperty("id", out var requestId))
                    {
                        if (name == "item/tool/call" && ToolHandler != null)
                        {
                            object output; bool success;
                            try { output = await ToolHandler(p.GetProperty("tool").GetString()!, p.GetProperty("arguments").Clone()); success = true; }
                            catch (Exception e) { output = new { error = e.GetBaseException().Message }; success = false; }
                            await Write(new { id = requestId.Clone(), result = new { success, contentItems = new[] { new { type = "inputText", text = JsonSerializer.Serialize(output) } } } }, lifetime.Token);
                        }
                        else await Write(new { id = requestId.Clone(), error = new { code = -32601, message = "This client permits only application-native tools." } }, lifetime.Token);
                    }
                    else if (name == "item/agentMessage/delta") Delta?.Invoke(p.GetProperty("delta").GetString() ?? "");
                    else if (name == "turn/completed")
                    {
                        var status = p.GetProperty("turn").GetProperty("status").GetString() ?? "failed";
                        Status?.Invoke("Turn " + status); completion?.TrySetResult(status);
                    }
                    else if (name == "error") Status?.Invoke("Codex reported an inference error. Check model access and usage in ChatGPT.");
                }
                else if (message.TryGetProperty("id", out var responseId) && responseId.TryGetInt64(out var id) && pending.TryGetValue(id, out var task))
                {
                    if (message.TryGetProperty("error", out var error)) task.TrySetException(new InvalidOperationException("Codex protocol request failed: " + error.GetProperty("message").GetString()));
                    else task.TrySetResult(message.GetProperty("result").Clone());
                }
            }
            throw new IOException("Codex app-server closed the connection.");
        }
        catch (Exception e)
        {
            foreach (var task in pending.Values) task.TrySetException(e);
            completion?.TrySetException(e); if (!lifetime.IsCancellationRequested) Status?.Invoke("Codex connection ended. Reconnect to resume.");
        }
    }
    public ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (process is { HasExited: false }) process.Kill(true); process?.Dispose();
        foreach (var p in pending.Values) p.TrySetCanceled(); completion?.TrySetCanceled(); return ValueTask.CompletedTask;
    }
}
