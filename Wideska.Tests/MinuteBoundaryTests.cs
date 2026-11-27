using Wideska.Models;
using Wideska.ViewModels;

internal static partial class Program
{
    private static void TestMinuteBoundary()
    {
        Check(new WidgetSettings().StartWithWindows, "New installations must enable startup after Windows sign-in by default");
        Check(MainViewModel.DelayUntilNextMinute(DateTimeOffset.Parse("2026-11-27T12:34:00Z")) == TimeSpan.FromMinutes(1),
            "An exact minute boundary must schedule the following minute without a busy loop");
        Check(MainViewModel.DelayUntilNextMinute(DateTimeOffset.Parse("2026-11-27T12:34:59.999Z")) == TimeSpan.FromMilliseconds(1),
            "Synchronization must align to the next second-zero boundary");
        foreach (string text in new[] { "2026-11-27T12:34:17.1234567Z", "2026-12-31T23:59:59.5+03:00", "2026-11-27T12:34:42-07:00" })
        {
            var now = DateTimeOffset.Parse(text);
            TimeSpan delay = MainViewModel.DelayUntilNextMinute(now);
            Check(delay > TimeSpan.Zero && delay <= TimeSpan.FromMinutes(1) && (now + delay).UtcTicks % TimeSpan.TicksPerMinute == 0,
                "Clock alignment must preserve sub-millisecond precision across offsets and date rollover");
        }
    }
}
