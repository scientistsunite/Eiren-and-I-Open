namespace Detector.History;

// UI defaults derive from persisted offsets, independent of the viewer's current zone.
// The selected recorded-local-date predicate still constrains returned observations.
public static class DiagnosticRange
{
    public static (DateTime FromUtc,DateTime ToUtc) ForDay(int day,HourRow[] hours)
    {
        var date=new DateTime(day/10000,day/100%100,day%100,0,0,0,DateTimeKind.Utc);
        if(hours.Length==0)return(date,date.AddDays(1));
        var first=hours.MinBy(h=>h.UtcTicks)!;var last=hours.MaxBy(h=>h.UtcTicks)!;
        var start=new DateTime(first.UtcTicks,DateTimeKind.Utc);var end=new DateTime(last.UtcTicks,DateTimeKind.Utc);
        int firstLocalHour=new DateTimeOffset(start).ToOffset(TimeSpan.FromMinutes(first.OffsetMinutes)).Hour;
        int lastLocalHour=new DateTimeOffset(end).ToOffset(TimeSpan.FromMinutes(last.OffsetMinutes)).Hour;
        return(start.AddHours(-firstLocalHour),end.AddHours(24-lastLocalHour));
    }
}
