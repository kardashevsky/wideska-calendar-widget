using System.IO;
using System.Text.Json;

namespace Wideska.Services;

public sealed record GoogleOAuthConfiguration(string ClientId, string ClientSecret)
{
    public static async Task<GoogleOAuthConfiguration> ReadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("installed", out var client))
            throw new InvalidOperationException("Choose the JSON file for an OAuth Desktop app, not a Web app or service account.");
        string id = client.TryGetProperty("client_id", out var idValue) ? idValue.GetString() ?? "" : "";
        string secret = client.TryGetProperty("client_secret", out var secretValue) ? secretValue.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id) || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
            throw new InvalidOperationException("This file does not contain a valid Google OAuth client ID.");
        return new(id, secret);
    }
}
