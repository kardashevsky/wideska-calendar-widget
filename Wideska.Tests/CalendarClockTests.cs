using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;
internal static partial class Program
{
    private static async Task TestCalendarClockAndPersistenceAsync(string path)
    {
        var store = new LocalStore(path);
        await store.WriteAsync("settings.bin", new WidgetSettings { SyncMinutes = 17 }, true);
        var provider = new ClockProvider();
        using (var vm = new MainViewModel(store, provider))
        {
            await vm.InitializeAsync(); vm.View = CalendarView.SingleDay;
            Check(vm.SyncMinutes == 1 && vm.Settings.SyncMinutes == 1, "Existing sync preferences must migrate to automatic one-minute synchronization");
            vm.UpdateCurrentTime(DateTimeOffset.Parse("2026-01-01T23:59Z")); vm.TodayCommand.Execute(null);
            Check(vm.CalendarTimeZoneId == "Asia/Tokyo" && vm.Days[0].Date == new DateTime(2026,1,2), "Today must follow primary calendar time zone rather than Windows date");
            Check(vm.ClockText == "08:59:00" && vm.IsSingleDay && vm.DayHeading.Contains("2"), "Day heading and second-resolution clock must use the calendar time zone");
            var timed = vm.Days[0].Events.Single(e => !e.AllDay);
            string originalLanguage = vm.Language;
            foreach (string language in new[] { "en", "ru" })
            {
                vm.Language = language;
                Check(vm.Days[0].Events.Single(e => !e.AllDay).Time == "08:00 \u2013 11:00",
                    "Event time must use zero-padded 24-hour hours in both interface languages");
            }
            vm.Language = originalLanguage;
            foreach (var view in new[] { CalendarView.Week, CalendarView.Month, CalendarView.Year })
            {
                vm.View = view;
                var cells = view == CalendarView.Year ? vm.Months.SelectMany(m => m.Days) : vm.Days;
                Check(cells.All(d => d.IsPast == (d.Date < new DateTime(2026, 1, 2))),
                    "Past days must use the calendar date in every view, including when UTC is still yesterday");
            }
            vm.View = CalendarView.SingleDay;
            Check(timed.StartMinute == 480 && timed.EndMinute == 660, "Event geometry must use the same calendar-local time as the current marker");
            Check(vm.Days[0].Events.Any(e => e.AllDay), "All-day dates must not shift when the display time zone changes");
            var day = vm.Days[0]; int requests = provider.Requests;
            vm.UpdateCurrentTime(DateTimeOffset.Parse("2026-01-02T00:00Z"));
            Check(ReferenceEquals(day,vm.Days[0]) && provider.Requests == requests && vm.CurrentTime.Minute == 0, "Minute tick must update time without reloading days or requesting Google data");
            vm.UpdateCurrentTime(DateTimeOffset.Parse("2026-01-02T15:00Z"));
            Check(vm.Days[0].Date == new DateTime(2026,1,3) && vm.Days[0].Events.Count == 0, "Calendar-zone midnight must advance today and preserve exclusive event ends");
            vm.GeometryLocked = true;
            vm.Settings.DesktopPlacement = new ScreenPlacement(-1600,80,1200,900);
            vm.IsDesktopFilled = true;
            vm.Settings.RestorePlacement = new ScreenPlacement(-1200,100,720,560);
            await vm.ShutdownAsync();
        }
        using (var restored = new MainViewModel(store,new ClockProvider()))
        {
            await restored.InitializeAsync();
            Check(restored.GeometryLocked && restored.Settings.DesktopPlacement == new ScreenPlacement(-1600,80,1200,900), "Restart must restore lock state and physical multi-monitor geometry");
            Check(restored.TimeZoneCalendarId == "tokyo", "Restart must restore the chosen calendar time zone");
            Check(restored.IsDesktopFilled && restored.Settings.RestorePlacement == new ScreenPlacement(-1200,100,720,560), "Restart must retain desktop-fill mode and the prior windowed bounds");
            await restored.ShutdownAsync();
        }
    }
    private sealed class ClockProvider : ICalendarProvider
    {
        public int Requests;
        public Task<bool> IsConnectedAsync(CancellationToken token) => Task.FromResult(true);
        public Task ConnectAsync(string id,string secret,CancellationToken token) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task<CalendarSnapshot> GetSnapshotAsync(DateTimeOffset start,DateTimeOffset end,CancellationToken token)
        {
            Requests++;
            return Task.FromResult(new CalendarSnapshot([new("tokyo","Tokyo","#9876CD","Asia/Tokyo",true)],
                [new("timed","tokyo","Meeting",DateTimeOffset.Parse("2026-01-01T23:00Z"),DateTimeOffset.Parse("2026-01-02T02:00Z"),false,"#9876CD","",""),
                 new("all","tokyo","All day",new DateTimeOffset(new DateTime(2026,1,2)),new DateTimeOffset(new DateTime(2026,1,3)),true,"#9876CD","","")],DateTimeOffset.Now,start,end));
        }
        public void Dispose() { }
    }
}
