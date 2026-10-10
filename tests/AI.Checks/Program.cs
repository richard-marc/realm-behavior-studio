using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Studio.AI;

var checks = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (InvalidOperationException) { Check(true, message); return; } throw new Exception("Expected rejection: " + message); }
var directory = Path.GetFullPath(".artifacts/auth-checks/" + Guid.NewGuid().ToString("N"));
var handler = new TokenPeer(); var accounts = new ChatGptAccounts(directory, new HttpClient(handler));
Check(accounts.HostId == new ChatGptAccounts(directory).HostId, "Host ID persists across launches");
var registration = Guid.NewGuid().ToString("N");
var p = new AccountProfile(registration, "test-issued-client", "https://auth.openai.com", "subject-1", "test@example.invalid", "fake-access", "fake-refresh", "fake-id", DateTimeOffset.UtcNow.AddMinutes(-1), ["resource.invoke", "chatgpt.tokens.use.direct"]);
var first = ChatGptAccounts.AuthorizationParameters(accounts.HostId, "http://127.0.0.1:12345/auth/callback", "state", "nonce", new string('a', 43));
Check(first["client_id"] == "dynamic_agent_client" && first["agent_name_hint"] == ChatGptAccounts.AppName && first["code_challenge_method"] == "S256", "Dynamic registration, app attribution and PKCE S256");
var returning = ChatGptAccounts.AuthorizationParameters(accounts.HostId, "http://127.0.0.1:12345/auth/callback", "state", "nonce", new string('a', 43), p);
Check(returning["client_id"] == p.ClientId && !returning.ContainsKey("agent_name_hint") && returning["id_token_hint"] == "fake-id", "Returning registration uses its issued ID and retained identity hint");
Reject(() => ChatGptAccounts.ValidateState("attacker", "expected"), "Invalid callback state rejected");
using var rsa = RSA.Create(2048); var pub = rsa.ExportParameters(false);
var jwks = JsonSerializer.SerializeToElement(new { keys = new[] { new { kid = "unit-key", kty = "RSA", n = ChatGptAccounts.Base64Url(pub.Modulus!), e = ChatGptAccounts.Base64Url(pub.Exponent!) } } });
string Token(string audience = "test-issued-client", string nonce = "nonce", long? expiry = null)
{
    var header = ChatGptAccounts.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "unit-key" }));
    var payload = ChatGptAccounts.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = "https://auth.openai.com", aud = audience, sub = "subject-1", email = "test@example.invalid", nonce, exp = expiry ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }));
    return header + "." + payload + "." + ChatGptAccounts.Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
var valid = Token();
Check(ChatGptAccounts.ValidateIdToken(valid, p.ClientId, "nonce", p.Issuer, jwks).Subject == p.Subject, "Valid signed identity accepted");
Reject(() => ChatGptAccounts.ValidateIdToken(Token("wrong"), p.ClientId, "nonce", p.Issuer, jwks), "Wrong identity audience rejected");
Reject(() => ChatGptAccounts.ValidateIdToken(Token(nonce: "wrong"), p.ClientId, "nonce", p.Issuer, jwks), "Wrong nonce rejected");
Reject(() => ChatGptAccounts.ValidateIdToken(Token(expiry: 1), p.ClientId, "nonce", p.Issuer, jwks), "Expired identity rejected");
var parts = valid.Split('.'); parts[1] = ChatGptAccounts.Base64Url(Encoding.UTF8.GetBytes("{\"iss\":\"attacker\"}"));
Reject(() => ChatGptAccounts.ValidateIdToken(string.Join('.', parts), p.ClientId, "nonce", p.Issuer, jwks), "Tampered identity signature rejected");
typeof(ChatGptAccounts).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(accounts, [p]);
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(directory, "*.profile").Single())).Contains("fake-refresh"), "Credentials protected with Windows DPAPI");
var renewed = await Task.WhenAll(accounts.RenewAsync(p, default), accounts.RenewAsync(p, default));
Check(handler.Refreshes == 1 && renewed.All(r => r.RefreshToken == "fake-rotated" && r.PlanEnabled), "Concurrent renewal serialized; rotated tokens and permissions preserved");
Check(await accounts.SignOutAsync(renewed[0], default), "Sign-out revokes the renewable session");
Check(accounts.Profiles().Single().AccessToken == null && accounts.Profiles().Single().ClientId == p.ClientId, "Sign-out clears credentials and preserves registration");
var fake = Path.GetFullPath("tests/FakeCodex/bin/Debug/net10.0/FakeCodex.exe");
await using var conversation = new CodexConversation(); var output = ""; var called = false;
conversation.Delta += text => output += text; conversation.ToolHandler = (name, args) => { called = name == "inspect_selection"; return Task.FromResult<object>(new { revision = 1 }); };
await conversation.StartAsync(fake, p, "test-model", [], default);
await conversation.SendAsync("complete", default);
Check(output == "streamed test output" && conversation.ThreadId == "thread-1", "Codex initialization, thread creation and streamed completion contract");
await conversation.SendAsync("tool", default); Check(called, "Native tool call contract returns typed content");
try { await conversation.SendAsync("fail", default); throw new Exception("Failed turn accepted"); } catch (InvalidOperationException) { Check(true, "Failed turn is not reported as completed"); }
var waiting = conversation.SendAsync("wait", default); await Task.Delay(100); await conversation.CancelAsync(default);
try { await waiting; throw new Exception("Canceled turn accepted"); } catch (InvalidOperationException) { Check(true, "Codex turn interruption handled"); }
Check(CodexConversation.ProviderArguments.Contains("model_providers.openai_chatgpt_plan.env_key=\"ACCESS_TOKEN\"") && CodexConversation.ProviderArguments.Contains("model_providers.openai_chatgpt_plan.supports_websockets=false"), "Official ChatGPT-plan provider configuration");
File.WriteAllText(".artifacts/auth-results.json", JsonSerializer.Serialize(new { checks, liveSignIn = "Not exercised; requires user browser consent", liveInference = "Not exercised; fake protocol peer only" }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count} AI contract checks passed. Live sign-in/inference remains unverified.");

sealed class TokenPeer : HttpMessageHandler
{
    public int Refreshes;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("openid-configuration")) return new(HttpStatusCode.OK) { Content = new StringContent("{\"revocation_endpoint\":\"https://auth.openai.com/mock/revoke\"}") };
        if (request.RequestUri.AbsolutePath.EndsWith("/revoke")) return new(HttpStatusCode.OK);
        Interlocked.Increment(ref Refreshes); await Task.Delay(150, cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake-new-access\",\"refresh_token\":\"fake-rotated\",\"token_type\":\"Bearer\",\"expires_in\":3600}") };
    }
}
