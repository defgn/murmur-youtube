using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WofflePlus.Cloud;

/// <summary>Supported cloud identities.</summary>
internal enum CloudBackend
{
    OpenAiKey,
    CodexSubscription,
    AnthropicKey,
    ZaiKey,
    DeepSeekKey,
}

/// <summary>A ChatGPT/Codex OAuth identity held in memory.</summary>
internal sealed record CodexTokens(
    string IdToken,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string AccountId)
{
    public bool NeedsRefresh => DateTimeOffset.UtcNow >= ExpiresAt - TimeSpan.FromMinutes(2);
}

/// <summary>Browser OAuth login compatible with the official Codex CLI file store.</summary>
internal sealed class CodexLogin : IDisposable
{
    // Public OAuth client used by the official Codex CLI.
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string AuthBase = "https://auth.openai.com";
    public const int RedirectPort = 1455;

    private HttpListener? _listener;

    public static string TokenCachePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "auth.json");
        }
    }

    public static bool IsSignedIn => TryLoadTokens() is not null;

    public static string BuildAuthUrl(string state, string codeVerifier)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var redirect = $"http://localhost:{RedirectPort}/auth/callback";
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirect,
            ["scope"] = "openid profile email offline_access api.connectors.read api.connectors.invoke",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = "codex_cli_rs",
        };
        return $"{AuthBase}/oauth/authorize?" + string.Join("&", query.Select(
            pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    public async Task<CodexTokens?> LoginAsync(Action<string> openBrowser, CancellationToken ct)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var redirect = $"http://localhost:{RedirectPort}/auth/callback";

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{RedirectPort}/auth/callback/");
        _listener.Start();
        try
        {
            openBrowser(BuildAuthUrl(state, verifier));
            var context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            var returnedState = context.Request.QueryString["state"];
            var valid = !string.IsNullOrWhiteSpace(code) && string.Equals(state, returnedState, StringComparison.Ordinal);
            await ReplyAsync(context, valid, ct).ConfigureAwait(false);
            if (!valid || code is null) return null;

            var response = await ExchangeCodeAsync(code, verifier, redirect, ct).ConfigureAwait(false);
            if (response?.AccessToken is null || response.RefreshToken is null || response.IdToken is null) return null;
            var tokens = FromResponse(response, response.RefreshToken, response.IdToken);
            SaveTokens(tokens);
            return tokens;
        }
        catch (Exception e) when (e is HttpListenerException or HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
        finally
        {
            StopListener();
        }
    }

    public static async Task<CodexTokens?> GetValidTokensAsync(CancellationToken ct)
    {
        var cached = TryLoadTokens();
        if (cached is null || !cached.NeedsRefresh) return cached;

        try
        {
            var response = await RefreshAsync(cached.RefreshToken, ct).ConfigureAwait(false);
            if (response?.AccessToken is null) return cached;
            var refresh = response.RefreshToken ?? cached.RefreshToken;
            var idToken = response.IdToken ?? cached.IdToken;
            var tokens = FromResponse(response, refresh, idToken);
            SaveTokens(tokens);
            return tokens;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return cached;
        }
    }

    public static void SignOut()
    {
        if (File.Exists(TokenCachePath)) File.Delete(TokenCachePath);
    }

    private static async Task<TokenResponse?> ExchangeCodeAsync(
        string code, string verifier, string redirect, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        });
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    private static async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        });
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    private static async Task<TokenResponse?> PostTokenAsync(HttpContent form, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(AuthBase), Timeout = TimeSpan.FromSeconds(45) };
        using var response = await http.PostAsync("oauth/token", form, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, TokenJsonContext.Default.TokenResponse);
    }

    private static CodexTokens FromResponse(TokenResponse response, string refreshToken, string idToken)
    {
        var expires = JwtExpiry(response.AccessToken!) ?? DateTimeOffset.UtcNow.AddHours(1);
        var accountId = JwtAccountId(idToken) ?? string.Empty;
        return new CodexTokens(idToken, response.AccessToken!, refreshToken, expires, accountId);
    }

    private static CodexTokens? TryLoadTokens()
    {
        if (!File.Exists(TokenCachePath)) return null;
        try
        {
            var json = File.ReadAllText(TokenCachePath);
            var auth = JsonSerializer.Deserialize(json, TokenJsonContext.Default.CodexAuthFile);
            var tokens = auth?.Tokens;
            if (tokens?.AccessToken is null || tokens.RefreshToken is null || tokens.IdToken is null) return null;
            return new CodexTokens(
                tokens.IdToken,
                tokens.AccessToken,
                tokens.RefreshToken,
                JwtExpiry(tokens.AccessToken) ?? DateTimeOffset.UtcNow.AddMinutes(30),
                tokens.AccountId ?? JwtAccountId(tokens.IdToken) ?? string.Empty);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveTokens(CodexTokens tokens)
    {
        var auth = new CodexAuthFile(
            "chatgpt",
            null,
            new CodexAuthTokens(tokens.IdToken, tokens.AccessToken, tokens.RefreshToken, tokens.AccountId),
            DateTimeOffset.UtcNow);
        File.WriteAllText(TokenCachePath, JsonSerializer.Serialize(auth, TokenJsonContext.Default.CodexAuthFile));
    }

    private static DateTimeOffset? JwtExpiry(string jwt)
    {
        using var payload = ParseJwt(jwt);
        if (payload is null || !payload.RootElement.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds)) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static string? JwtAccountId(string jwt)
    {
        using var payload = ParseJwt(jwt);
        if (payload is null || !payload.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth)) return null;
        return auth.TryGetProperty("chatgpt_account_id", out var value) ? value.GetString() : null;
    }

    private static JsonDocument? ParseJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var text = parts[1].Replace('-', '+').Replace('_', '/');
            text += new string('=', (4 - text.Length % 4) % 4);
            return JsonDocument.Parse(Convert.FromBase64String(text));
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }
    }

    private static async Task ReplyAsync(HttpListenerContext context, bool ok, CancellationToken ct)
    {
        var colour = ok ? "#3fb950" : "#f0883e";
        var message = ok ? "Woffle+ is signed in. You can close this tab." : "Sign-in failed or was cancelled.";
        var html = $"<html><body style='font-family:sans-serif;background:#0d1117;color:{colour};display:grid;place-items:center;height:100%'><h2>{message}</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        context.Response.Close();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void StopListener()
    {
        if (_listener is null) return;
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        _listener = null;
    }

    public void Dispose() => StopListener();
}

internal sealed record TokenResponse(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);

internal sealed record CodexAuthFile(
    [property: JsonPropertyName("auth_mode")] string? AuthMode,
    [property: JsonPropertyName("OPENAI_API_KEY")] string? OpenAiApiKey,
    [property: JsonPropertyName("tokens")] CodexAuthTokens? Tokens,
    [property: JsonPropertyName("last_refresh")] DateTimeOffset? LastRefresh);

internal sealed record CodexAuthTokens(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("account_id")] string? AccountId);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(CodexAuthFile))]
internal sealed partial class TokenJsonContext : JsonSerializerContext;
