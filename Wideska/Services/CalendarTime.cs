using Wideska.Models;

namespace Wideska.Services;

public static class CalendarTime
{
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows)) return TimeZoneInfo.FindSystemTimeZoneById(windows);
            return TimeZoneInfo.Local;
        }
    }
    public static DateTime Local(DateTimeOffset instant, string? id) => TimeZoneInfo.ConvertTime(instant, Resolve(id)).DateTime;
    public static double? IndicatorMinutes(DateTime day, DateTimeOffset now, string? id)
    {
        var local = Local(now, id);
        return day.Date == local.Date ? local.Hour * 60 + local.Minute : null;
    }
    public static DateTimeOffset Midnight(DateTime date, string? id)
    {
        var zone = Resolve(id); var local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return new(local, zone.GetUtcOffset(local));
    }
}

