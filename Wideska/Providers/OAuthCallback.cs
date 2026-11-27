using System.Text;
using System.IO;

namespace Wideska.Providers;

internal static class OAuthCallback
{
    private const int MaximumRequestLineBytes = 8192;

    internal static async Task<string?> ReadRequestLineAsync(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[MaximumRequestLineBytes];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(count), token);
            if (read == 0) return null;
            int end = Array.IndexOf(buffer, (byte)'\n', count, read);
            if (end >= 0) return Encoding.ASCII.GetString(buffer, 0, end).TrimEnd('\r');
            count += read;
        }
        return null;
    }

    internal static bool TryParse(string? requestLine, string redirect, string expectedState, out Dictionary<string, string> values)
    {
        values = new(StringComparer.Ordinal);
        if (requestLine is null || requestLine.Length > MaximumRequestLineBytes) return false;
        string[] parts = requestLine.Split(' ');
        if (parts.Length != 3 || parts[0] != "GET" || parts[2] is not ("HTTP/1.0" or "HTTP/1.1")
            || !parts[1].StartsWith('/') || parts[1].StartsWith("//", StringComparison.Ordinal)) return false;
        var origin = new Uri(redirect);
        if (!Uri.TryCreate(origin, parts[1], out var callback) || callback.AbsolutePath != "/" || callback.Fragment.Length != 0
            || callback.Scheme != origin.Scheme || !string.Equals(callback.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string pair in callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] item = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(item[0]);
            string value = item.Length > 1 ? Uri.UnescapeDataString(item[1].Replace('+', ' ')) : "";
            if (!values.TryAdd(key, value)) return false;
        }
        return values.GetValueOrDefault("state") == expectedState;
    }
}
