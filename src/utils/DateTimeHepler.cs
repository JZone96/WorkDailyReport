using WorkDailyReport.Config;

namespace WorkDailyReport.utils;

public static class DateTimeHepler
{
    public static (DateTimeOffset since, DateTimeOffset until) TodayWindow(WorkHoursOptions opt)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(opt.TimeZone);

        var todayLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date;

        var startLocal = todayLocal.Add(TimeOnly.Parse(opt.Start).ToTimeSpan());
        var endLocal   = todayLocal.Add(TimeOnly.Parse(opt.End).ToTimeSpan());

        // Offset corretto per la data specifica (gestisce l’ora legale)
        var since = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal));
        var until = new DateTimeOffset(endLocal,   tz.GetUtcOffset(endLocal));

        return (since, until);
    }
}
