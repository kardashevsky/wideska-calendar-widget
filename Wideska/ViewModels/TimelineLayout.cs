namespace Wideska.ViewModels;
public sealed record TimedPlacement(EventItem Event, int Lane, int LaneCount);
public static class TimelineLayout
{
    public static IReadOnlyList<TimedPlacement> Arrange(IEnumerable<EventItem> events)
    {
        var result = new List<TimedPlacement>();
        var group = new List<(EventItem Event, int Lane)>();
        var ends = new List<double>(); double groupEnd = -1;
        void Flush()
        {
            result.AddRange(group.Select(e => new TimedPlacement(e.Event, e.Lane, ends.Count)));
            group.Clear(); ends.Clear(); groupEnd = -1;
        }
        foreach (var item in events.Where(e => !e.AllDay).OrderBy(e => e.StartMinute).ThenByDescending(e => e.EndMinute))
        {
            if (item.StartMinute >= groupEnd) Flush();
            int lane = ends.FindIndex(end => end <= item.StartMinute);
            if (lane < 0) { lane = ends.Count; ends.Add(item.EndMinute); } else ends[lane] = item.EndMinute;
            group.Add((item, lane)); groupEnd = Math.Max(groupEnd, item.EndMinute);
        }
        Flush(); return result;
    }
}

