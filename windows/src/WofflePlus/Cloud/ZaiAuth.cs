using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WofflePlus.Cloud;

/// <summary>
/// Builds z.ai JWT auth tokens from an id.secret API key pair, exactly the shape their
/// docs generate: HS256 over {api_key, exp, timestamp} with an alg+sign_type header.
/// Tokens are short-lived (60s margin) and held in memory only.
/// </summary>
internal static class ZaiAuth
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>A signed token for the id.secret pair, or null when either half is missing.</summary>
    public static string? BuildToken(string? keyId, string? keySecret)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret)) return null;

        var issuedAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["api_key"] = keyId,
            ["exp"] = issuedAt.Add(Lifetime).ToUnixTimeMilliseconds(),
            ["timestamp"] = issuedAt.ToUnixTimeMilliseconds(),
        });

        var header = "{\"alg\":\"HS256\",\"sign_type\":\"SIGN\"}";
        var unsigned = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(keySecret), Encoding.UTF8.GetBytes(unsigned));
        return unsigned + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
