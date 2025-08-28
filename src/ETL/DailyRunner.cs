using Microsoft.Extensions.Options;
using WorkDailyReport.ActivityWatch;
using WorkDailyReport.Config;
using WorkDailyReport.utils;

namespace WorkDailyReport.ETL;

public sealed class DailyRunner
{
    private readonly IActivityWatchClient _aw;
    private readonly IGitRepoLocator _repoLocator;
    private readonly WorkReportOptions _opt;

    public DailyRunner(
        IActivityWatchClient aw,
        IGitRepoLocator repoLocator,
        IOptions<WorkReportOptions> opt)
    {
        _aw = aw;
        _repoLocator = repoLocator;
        _opt = opt.Value;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var (since, until, startDateOnly, endDateOnly) = GetWindow(_opt.WorkHours, _opt.ReportWindow);

        var buckets = await _aw.ListBucketsAsync(ct);
        if (buckets.Count == 0)
        {
            Console.WriteLine("⚠ Nessun bucket. Avvia ActivityWatch.");
            return;
        }

        var preferredWatchers = _opt.ActivityWatch.Watchers ?? new List<string>();
        var windowBucket = buckets.FirstOrDefault(b =>
            preferredWatchers.Any(w => b.Id.Contains(w, StringComparison.OrdinalIgnoreCase)))
            ?? buckets.FirstOrDefault(b => b.Id.Contains("aw-watcher-window", StringComparison.OrdinalIgnoreCase));

        if (windowBucket is null)
        {
            Console.WriteLine("⚠ Nessun bucket window.");
            return;
        }

        Console.WriteLine($"Intervallo: {since}  →  {until}");

        // ActivityWatch: stessi since/until
        var events = await _aw.GetEventsAsync(windowBucket.Id, since, until, ct);
        Console.WriteLine($"Eventi: {events.Count}");
        foreach (var ev in events.Take(10))
            Console.WriteLine($"- {ev.Timestamp} ({ev.Duration ?? 0}s) → {ev.Data.App} | {ev.Data.Title} | {ev.Data.Url}");

        // Git: usa le stesse date (solo parte data)
        var repos = await _repoLocator.FindAsync(_opt.Paths.ReposRoot, ct);
        foreach (var r in repos)
        {
            Console.WriteLine($"Repo trovata: {r}");
            var commits = await GitHelper.GetCommitsByDate(r, ct, startDateOnly, endDateOnly);
            foreach (var c in commits)
                Console.WriteLine($"  {c.Date:t} {c.Author} {c.Message} ({c.Hash[..7]})");
        }
    }

    private static (DateTimeOffset since, DateTimeOffset until, DateTime startDateOnly, DateTime endDateOnly)
    GetWindow(WorkHoursOptions wh, ReportWindowOptions? rw)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(wh.TimeZone);

        // Se configurato, usa le date fisse; altrimenti oggi
        DateTime startDate = DateTime.Today;
        DateTime endDate = DateTime.Today;

        if (!string.IsNullOrWhiteSpace(rw?.StartDate) &&
            !string.IsNullOrWhiteSpace(rw?.EndDate) &&
            DateTime.TryParse(rw!.StartDate, out var sd) &&
            DateTime.TryParse(rw!.EndDate, out var ed))
        {
            startDate = sd.Date;
            endDate = ed.Date;
        }

        // TimeOnly → istanti locali
        var startTime = TimeOnly.Parse(wh.Start);
        var endTime = TimeOnly.Parse(wh.End);

        var startLocal = startDate.Add(startTime.ToTimeSpan());
        var endLocal = endDate.Add(endTime.ToTimeSpan());

        var since = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal));
        var until = new DateTimeOffset(endLocal, tz.GetUtcOffset(endLocal));

        // Per git usiamo le sole date (git --since/--until gestirà i bound)
        return (since, until, startDate, endDate);
    }
}
