using System.Text.Json;
void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
while (Console.ReadLine() is { } line)
{
    using var json = JsonDocument.Parse(line); var request = json.RootElement;
    if (!request.TryGetProperty("method", out var m))
    {
        if (request.TryGetProperty("id", out var call) && call.ValueKind == JsonValueKind.String && call.GetString() == "tool-1")
            Emit(new { method = "turn/completed", @params = new { turn = new { id = "turn-1", status = "completed" } } });
        continue;
    }
    var method = m.GetString();
    if (!request.TryGetProperty("id", out var id)) continue;
    var resultId = id.Clone();
    switch (method)
    {
        case "initialize": Emit(new { id = resultId, result = new { userAgent = "fake-contract-peer" } }); break;
        case "thread/start":
        case "thread/resume": Emit(new { id = resultId, result = new { thread = new { id = "thread-1" } } }); break;
        case "turn/start":
            var text = request.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString();
            Emit(new { id = resultId, result = new { turn = new { id = "turn-1", status = "inProgress" } } });
            Emit(new { method = "item/agentMessage/delta", @params = new { threadId = "thread-1", turnId = "turn-1", delta = "streamed test output" } });
            if (text == "tool") Emit(new { id = "tool-1", method = "item/tool/call", @params = new { threadId = "thread-1", turnId = "turn-1", tool = "inspect_selection", arguments = new { } } });
            else if (text != "wait") Emit(new { method = "turn/completed", @params = new { turn = new { id = "turn-1", status = text == "fail" ? "failed" : "completed" } } });
            break;
        case "turn/interrupt":
            Emit(new { id = resultId, result = new { } });
            Emit(new { method = "turn/completed", @params = new { turn = new { id = "turn-1", status = "interrupted" } } }); break;
        default: Emit(new { id = resultId, error = new { code = -32601, message = "unknown method" } }); break;
    }
}
