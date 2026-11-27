using System.Net;
using System.Net.Http;
using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;
internal static partial class Program
{
    private static async Task TestGoogleProviderAsync(LocalStore store)
    {
        await store.WriteAsync("google-token.bin", new GoogleCalendarProvider.TokenData("expired", "refresh", DateTimeOffset.UtcNow.AddHours(-1), "test-client", "test-secret"), true);
        var handler = new GoogleHandler();
        using var provider = new GoogleCalendarProvider(store, new HttpClient(handler));
        var start = new DateTimeOffset(new DateTime(2026, 1, 1));
        var result = await provider.GetSnapshotAsync(start, start.AddYears(1), default);
        Check(handler.RefreshRequests == 1, "Expired access token must refresh once");
        Check(handler.CalendarRequests == 2 && handler.EventRequests == 2, "Google calendar and event pagination");
        Check(result.Events.Count == 2, "Cancelled recurring instances must be omitted");
        Check(result.Events[0].AllDay && result.Events[0].End.Day == 27, "Google all-day dates must preserve exclusive end");
        Check(result.Events[1].Title == "(Untitled)", "Missing event titles must have a fallback");
        Check(result.RangeStart == start, "Cache coverage must match requested interval");
        Check(result.Calendars[0].TimeZoneId == "America/New_York" && result.Calendars[0].IsPrimary, "Calendar API metadata must supply primary calendar and IANA time zone");
        var token = await store.ReadAsync<GoogleCalendarProvider.TokenData>("google-token.bin", true);
        Check(token?.AccessToken == "fresh" && token.RefreshToken == "refresh", "Refresh must preserve and encrypt refresh token");
        handler.Forbidden = true;
        try { await provider.GetSnapshotAsync(start, start.AddYears(1), default); Check(false, "Forbidden response must fail"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("403"), "Google API errors must be actionable"); }
        handler.Forbidden = false;
        handler.Unauthorized = true;
        try { await provider.GetSnapshotAsync(start, start.AddYears(1), default); Check(false, "Revoked consent must fail"); }
        catch (CalendarAuthorizationException) { Check(true, "HTTP 401 must request authorization rather than expose cached events"); }
        await provider.DisconnectAsync(default);
        Check(!await provider.IsConnectedAsync(default), "Disconnect must remove local credentials");
    }
    private sealed class GoogleHandler : HttpMessageHandler
    {
        public int RefreshRequests, CalendarRequests, EventRequests;
        public bool Forbidden;
        public bool Unauthorized;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            string json;
            if (path == "/token") { RefreshRequests++; json = """{"access_token":"fresh","expires_in":3600}"""; }
            else if (path == "/revoke") json = "{}";
            else
            {
                Check(request.Headers.Authorization?.Parameter == "fresh", "Google requests must use refreshed bearer token");
                if (Forbidden) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                if (Unauthorized) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                if (path.EndsWith("calendarList"))
                {
                    CalendarRequests++;
                    json = request.RequestUri.Query.Contains("pageToken") ? """{"items":[]}""" : """{"items":[{"id":"work","summary":"Work","backgroundColor":"#92ACFF","timeZone":"America/New_York","primary":true}],"nextPageToken":"page2"}""";
                }
                else
                {
                    EventRequests++;
                    Check(request.RequestUri.Query.Contains("singleEvents=true") && request.RequestUri.Query.Contains("timeMin="), "Recurring events must request bounded expanded instances");
                    json = request.RequestUri.Query.Contains("pageToken")
                        ? """{"items":[{"id":"instance2","start":{"dateTime":"2026-11-26T10:00:00+03:00"},"end":{"dateTime":"2026-11-26T11:00:00+03:00"}},{"id":"cancelled","status":"cancelled"}]}"""
                        : """{"items":[{"id":"instance1","summary":"All day","start":{"date":"2026-11-26"},"end":{"date":"2026-11-27"}}],"nextPageToken":"events2"}""";
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
