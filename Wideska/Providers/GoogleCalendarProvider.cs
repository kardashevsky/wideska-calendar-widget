using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wideska.Models;
using Wideska.Services;

namespace Wideska.Providers;
public sealed class GoogleCalendarProvider(LocalStore store, HttpClient? client = null) : ICalendarProvider
{
    private const string TokenFile = "google-token.bin";
    private readonly HttpClient http = client ?? new() { Timeout = TimeSpan.FromSeconds(45) };
    private TokenData? credentials;
    public sealed record TokenData(string AccessToken, string RefreshToken, DateTimeOffset Expires, string ClientId, string ClientSecret);
    public async Task<bool> IsConnectedAsync(CancellationToken token)
    {
        credentials ??= await store.ReadAsync<TokenData>(TokenFile, true, token);
        return credentials is not null;
    }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task ConnectAsync(string clientId, string clientSecret, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Enter a Google Desktop OAuth client ID in Settings.");
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        string query = Query(new() { ["client_id"] = clientId.Trim(), ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "https://www.googleapis.com/auth/calendar.readonly", ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state, ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256" });
        Process.Start(new ProcessStartInfo("https://accounts.google.com/o/oauth2/v2/auth?" + query) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        string? code = null;
        while (code is null)
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            string? line = await reader.ReadLineAsync(timeout.Token);
            if (line is null) continue;
            string target = line.Split(' ').ElementAtOrDefault(1) ?? "/";
            var callback = new Uri(redirect.TrimEnd('/') + target);
            var values = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");
            bool valid = values.GetValueOrDefault("state") == state;
            string message = valid ? "Wideska authorization received. You can close this tab." : "Invalid authorization state.";
            byte[] body = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            if (!valid) continue;
            if (values.ContainsKey("error")) throw new InvalidOperationException("Google authorization was declined.");
            code = values.GetValueOrDefault("code") ?? throw new InvalidOperationException("Google returned no authorization code.");
        }
        credentials = await ExchangeAsync(new() { ["code"] = code, ["client_id"] = clientId.Trim(), ["client_secret"] = clientSecret.Trim(), ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code", ["code_verifier"] = verifier }, clientId.Trim(), clientSecret.Trim(), "", token);
        await store.WriteAsync(TokenFile, credentials, true, token);
    }
    private async Task<TokenData> ExchangeAsync(Dictionary<string, string> fields, string clientId, string secret, string refresh, CancellationToken token)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(fields), token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Google authorization failed ({(int)response.StatusCode}). Check your OAuth settings or reconnect.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = document.RootElement;
        return new(root.GetProperty("access_token").GetString()!, root.TryGetProperty("refresh_token", out var r) ? r.GetString()! : refresh,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60), clientId, secret);
    }
    private async Task EnsureTokenAsync(CancellationToken token)
    {
        if (!await IsConnectedAsync(token)) throw new InvalidOperationException("Connect Google Calendar in Settings.");
        if (credentials!.Expires > DateTimeOffset.UtcNow) return;
        credentials = await ExchangeAsync(new() { ["client_id"] = credentials.ClientId, ["client_secret"] = credentials.ClientSecret, ["refresh_token"] = credentials.RefreshToken, ["grant_type"] = "refresh_token" }, credentials.ClientId, credentials.ClientSecret, credentials.RefreshToken, token);
        await store.WriteAsync(TokenFile, credentials, true, token);
    }
    public async Task DisconnectAsync(CancellationToken token)
    {
        // Remove local credentials even if revocation cannot be reached while offline.
        var old = credentials ?? await store.ReadAsync<TokenData>(TokenFile, true, token);
        credentials = null;
        await store.DeleteAsync(TokenFile);
        if (old is null) return;
        try { using var response = await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = old.RefreshToken }), token); }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }
    public async Task<CalendarSnapshot> GetSnapshotAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        await EnsureTokenAsync(token);
        List<CalendarInfo> calendars = [];
        string? page = null;
        do
        {
            using var doc = await GetAsync("users/me/calendarList?maxResults=250" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), token);
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
                calendars.Add(new(Text(item, "id"), Text(item, "summary"), Text(item, "backgroundColor", "#7C9AFF")));
            page = Text(doc.RootElement, "nextPageToken", "");
        } while (!string.IsNullOrEmpty(page));
        List<CalendarEvent> events = [];
        foreach (var calendar in calendars)
        {
            page = null;
            do
            {
                string path = "calendars/" + Uri.EscapeDataString(calendar.Id) + "/events?" + Query(new() { ["singleEvents"] = "true", ["orderBy"] = "startTime", ["maxResults"] = "2500", ["timeMin"] = start.ToString("O"), ["timeMax"] = end.ToString("O") }) + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
                using var doc = await GetAsync(path, token);
                foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
                {
                    if (Text(item, "status") == "cancelled" || !item.TryGetProperty("start", out var s) || !item.TryGetProperty("end", out var e)) continue;
                    bool allDay = s.TryGetProperty("date", out _);
                    events.Add(new(Text(item, "id"), calendar.Id, Text(item, "summary", "(Untitled)"), ParseTime(s), ParseTime(e), allDay, calendar.Color, Text(item, "location"), Text(item, "description")));
                }
                page = Text(doc.RootElement, "nextPageToken", "");
            } while (!string.IsNullOrEmpty(page));
        }
        return new(calendars, events, DateTimeOffset.Now, start, end);
    }
    private async Task<JsonDocument> GetAsync(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials!.AccessToken);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Google Calendar returned {(int)response.StatusCode}. Check your connection and account permissions.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    private static string Text(JsonElement element, string name, string fallback = "") => element.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;
    private static DateTimeOffset ParseTime(JsonElement value) => value.TryGetProperty("dateTime", out var dt)
        ? DateTimeOffset.Parse(dt.GetString()!, CultureInfo.InvariantCulture).ToLocalTime()
        : new DateTimeOffset(DateTime.ParseExact(value.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    private static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    public void Dispose() => http.Dispose();
}
