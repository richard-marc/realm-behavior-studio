using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Studio.AI;

public sealed record AccountProfile(string RegistrationId, string ClientId, string Issuer, string Subject, string Email,
    string? AccessToken, string? RefreshToken, string? IdToken, DateTimeOffset ExpiresAt, string[] Scopes)
{
    public override string ToString() => Email + " · " + RegistrationId[..8];
    public bool PlanEnabled => Scopes.Contains("chatgpt.tokens.use.direct") && Scopes.Contains("resource.invoke");
}

// Public-client flow from the official OSS SIWC documentation. No partner secret or API key.
public sealed class ChatGptAccounts
{
    const string Auth = "https://auth.openai.com";
    const string Resource = "https://api.openai.com/v1";
    public const string AppName = "realm_behavior_studio";
    readonly string directory;
    readonly HttpClient http;
    public string HostId { get; }
    public ChatGptAccounts(string? path = null, HttpClient? client = null)
    {
        directory = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "ChatGPT");
        Directory.CreateDirectory(directory); http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var host = Path.Combine(directory, "host-id.txt");
        // CreateNew ensures simultaneous initial launches cannot overwrite the persistent host identity.
        if (!File.Exists(host))
        {
            try { using var stream = new FileStream(host, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream); writer.Write("urn:uuid:" + Guid.NewGuid()); }
            catch (IOException) when (File.Exists(host)) { }
        }
        HostId = File.ReadAllText(host);
    }
    string ProfilePath(string id) => Path.Combine(directory, Guid.Parse(id).ToString("N") + ".profile");
    public IReadOnlyList<AccountProfile> Profiles() => Directory.EnumerateFiles(directory, "*.profile").Select(Read).ToArray();
    AccountProfile Read(string path) => JsonSerializer.Deserialize<AccountProfile>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidOperationException("Invalid account record.");
    void Save(AccountProfile profile)
    {
        var path = ProfilePath(profile.RegistrationId); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(profile), null, DataProtectionScope.CurrentUser)); File.Move(temp, path, true);
    }
    static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    public static void ValidateState(string? actual, string expected)
    {
        if (actual == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected))) throw new InvalidOperationException("Sign-in state did not match. The attempt was rejected.");
    }
    public static Dictionary<string, string> AuthorizationParameters(string hostId, string redirect, string state, string nonce, string verifier, AccountProfile? existing = null)
    {
        var p = new Dictionary<string, string>
        {
            ["client_id"] = existing?.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = redirect,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", ["resource"] = Resource,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        };
        if (existing == null) p["agent_name_hint"] = AppName;
        else { if (existing.IdToken != null) p["id_token_hint"] = existing.IdToken; if (existing.Email.Length > 0) p["login_hint"] = existing.Email; }
        return p;
    }
    public async Task<AccountProfile> SignInAsync(AccountProfile? existing, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var guard = existing == null ? null : await SessionLock(existing.RegistrationId, timeout.Token);
        using var listener = new HttpListener();
        string redirect = "";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            redirect = $"http://127.0.0.1:{port}/auth/callback"; listener.Prefixes.Clear(); listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); break; } catch (HttpListenerException) when (attempt < 9) { }
        }
        var state = RandomValue(); var nonce = RandomValue(); var verifier = RandomValue();
        var parameters = AuthorizationParameters(HostId, redirect, state, nonce, verifier, existing);
        var uri = Auth + "/api/accounts/authorize?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        using var cancelListener = timeout.Token.Register(listener.Stop);
        HttpListenerContext context;
        try
        {
            while (true)
            {
                context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                if (context.Request.Url?.AbsolutePath == "/auth/callback") break;
                context.Response.StatusCode = 404; context.Response.Close();
            }
        }
        catch (HttpListenerException) when (timeout.IsCancellationRequested) { throw new OperationCanceledException(timeout.Token); }
        var query = context.Request.QueryString;
        try
        {
            ValidateState(query["state"], state);
            if (query["error"] != null) throw new InvalidOperationException("ChatGPT authorization was declined or failed. No credentials were replaced.");
            var issued = query["client_id"] ?? existing?.ClientId;
            if (string.IsNullOrWhiteSpace(issued) || issued == "dynamic_agent_client" || existing != null && issued != existing.ClientId) throw new InvalidOperationException("The issued registration ID was missing or did not match the selected account.");
            var code = query["code"] ?? throw new InvalidOperationException("Authorization code was missing.");
            var fields = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = issued, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource };
            var response = await TokenAsync(fields, timeout.Token);
            var idToken = response.GetProperty("id_token").GetString() ?? throw new InvalidOperationException("Identity token was missing.");
            var identity = await ValidateIdTokenAsync(idToken, issued, nonce, timeout.Token);
            if (existing != null && (identity.Issuer != existing.Issuer || identity.Subject != existing.Subject)) throw new InvalidOperationException("The signed-in account did not match the selected registration.");
            var profile = FromTokenResponse(response, existing?.RegistrationId ?? Guid.NewGuid().ToString("N"), issued, identity.Issuer, identity.Subject, identity.Email, null);
            Save(profile);
            context.Response.ContentType = "text/html; charset=utf-8";
            var body = Encoding.UTF8.GetBytes("<html><body><h2>Realm Behavior Studio is connected.</h2><p>You can close this tab and return to the application.</p></body></html>");
            await context.Response.OutputStream.WriteAsync(body, timeout.Token);
            return profile;
        }
        catch { context.Response.StatusCode = 400; throw; }
        finally { context.Response.Close(); listener.Stop(); }
    }
    public static AccountProfile FromTokenResponse(JsonElement response, string registration, string client, string issuer, string subject, string email, AccountProfile? previous)
    {
        var access = response.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Access token was missing.");
        if (response.TryGetProperty("token_type", out var tt) && !string.Equals(tt.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsupported token type.");
        var scopes = response.TryGetProperty("scope", out var scope) ? scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries) : previous?.Scopes ?? [];
        return new(registration, client, issuer, subject, email, access,
            response.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : previous?.RefreshToken,
            response.TryGetProperty("id_token", out var id) ? id.GetString() : previous?.IdToken,
            DateTimeOffset.UtcNow.AddSeconds(response.GetProperty("expires_in").GetInt32()), scopes);
    }
    async Task<JsonElement> TokenAsync(Dictionary<string, string> fields, CancellationToken token)
    {
        using var response = await http.PostAsync(Auth + "/api/accounts/oauth/token", new FormUrlEncodedContent(fields), token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode == HttpStatusCode.BadRequest ? "The session was revoked or the authorization grant expired. Sign in again." : "ChatGPT token service is unavailable. Try again later.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return json.RootElement.Clone();
    }
    public async Task<(string Issuer, string Subject, string Email)> ValidateIdTokenAsync(string jwt, string clientId, string nonce, CancellationToken token)
    {
        using var discovery = JsonDocument.Parse(await http.GetStringAsync(Auth + "/.well-known/openid-configuration", token));
        var issuer = discovery.RootElement.GetProperty("issuer").GetString()!;
        var jwks = new Uri(discovery.RootElement.GetProperty("jwks_uri").GetString()!);
        if (issuer != Auth || jwks.Scheme != "https" || jwks.Host != "auth.openai.com") throw new InvalidOperationException("Unexpected identity provider discovery response.");
        using var keys = JsonDocument.Parse(await http.GetStringAsync(jwks, token));
        return ValidateIdToken(jwt, clientId, nonce, issuer, keys.RootElement);
    }
    public static (string Issuer, string Subject, string Email) ValidateIdToken(string jwt, string clientId, string nonce, string issuer, JsonElement jwks)
    {
        var parts = jwt.Split('.'); if (parts.Length != 3) throw new InvalidOperationException("Invalid identity token.");
        using var header = JsonDocument.Parse(Decode(parts[0])); using var payload = JsonDocument.Parse(Decode(parts[1]));
        if (header.RootElement.GetProperty("alg").GetString() != "RS256") throw new InvalidOperationException("Unsupported identity signature algorithm.");
        var key = jwks.GetProperty("keys").EnumerateArray().Single(k => k.GetProperty("kid").GetString() == header.RootElement.GetProperty("kid").GetString());
        using var rsa = RSA.Create(); rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidOperationException("Identity signature was invalid.");
        var claims = payload.RootElement; var audience = claims.GetProperty("aud");
        var match = audience.ValueKind == JsonValueKind.Array ? audience.EnumerateArray().Any(a => a.GetString() == clientId) : audience.GetString() == clientId;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!match || claims.GetProperty("iss").GetString() != issuer || claims.GetProperty("exp").GetInt64() <= now || claims.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now + 30) throw new InvalidOperationException("Identity issuer, audience, or token lifetime did not match.");
        if (audience.ValueKind == JsonValueKind.Array && audience.GetArrayLength() > 1 && (!claims.TryGetProperty("azp", out var azp) || azp.GetString() != clientId)) throw new InvalidOperationException("Identity authorized party did not match.");
        ValidateState(claims.GetProperty("nonce").GetString(), nonce);
        var subject = claims.GetProperty("sub").GetString(); if (string.IsNullOrWhiteSpace(subject)) throw new InvalidOperationException("Identity subject was missing.");
        return (issuer, subject, claims.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "");
    }
    async Task<FileStream> SessionLock(string registration, CancellationToken token)
    {
        var path = ProfilePath(registration) + ".lock";
        while (true) { token.ThrowIfCancellationRequested(); try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { await Task.Delay(100, token); } }
    }
    public async Task<AccountProfile> RenewAsync(AccountProfile selected, CancellationToken token)
    {
        using var guard = await SessionLock(selected.RegistrationId, token);
        var profile = Read(ProfilePath(selected.RegistrationId));
        if (profile.AccessToken == null || profile.RefreshToken == null) throw new InvalidOperationException("Sign in to this account again.");
        if (profile.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return profile;
        var response = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = profile.ClientId, ["refresh_token"] = profile.RefreshToken, ["resource"] = Resource }, token);
        var renewed = FromTokenResponse(response, profile.RegistrationId, profile.ClientId, profile.Issuer, profile.Subject, profile.Email, profile); Save(renewed); return renewed;
    }
    public async Task<bool> SignOutAsync(AccountProfile profile, CancellationToken token)
    {
        using var guard = await SessionLock(profile.RegistrationId, token);
        var latest = Read(ProfilePath(profile.RegistrationId)); var confirmed = latest.RefreshToken == null;
        try
        {
            if (latest.RefreshToken != null)
            {
                using var discovery = JsonDocument.Parse(await http.GetStringAsync(Auth + "/.well-known/openid-configuration", token));
                var endpoint = new Uri(discovery.RootElement.GetProperty("revocation_endpoint").GetString()!);
                if (endpoint.Scheme != "https" || endpoint.Host != "auth.openai.com") throw new InvalidOperationException("Unexpected revocation endpoint.");
                using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = latest.ClientId, ["token"] = latest.RefreshToken, ["token_type_hint"] = "refresh_token" }), token);
                confirmed = response.IsSuccessStatusCode;
            }
        }
        catch (HttpRequestException) { confirmed = false; }
        catch (TaskCanceledException) { confirmed = false; }
        finally { Save(latest with { AccessToken = null, RefreshToken = null, IdToken = null, Scopes = [], ExpiresAt = DateTimeOffset.MinValue }); }
        return confirmed;
    }
}
