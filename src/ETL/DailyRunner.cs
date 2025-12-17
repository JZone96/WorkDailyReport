using Microsoft.Extensions.Options;
using WorkDailyReport.ActivityWatch;
using WorkDailyReport.Config;
using WorkDailyReport.utils;
using System.Globalization;
using System.Linq;

namespace WorkDailyReport.ETL;

public sealed class DailyRunner
{
    private readonly IActivityWatchClient _aw;
    private readonly IGitCommitSource _gitCommitSource;
    private readonly WorkReportOptions _opt;

    public DailyRunner(
        IActivityWatchClient aw,
        IGitCommitSource gitCommitSource,
        IOptions<WorkReportOptions> opt)
    {
        _aw = aw;
        _gitCommitSource = gitCommitSource;
        _opt = opt.Value;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var (since, untilExclusive, gitStartDate, gitEndDateExclusive) =
            GetWindow(_opt.WorkHours, _opt.ReportWindow);

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

        Console.WriteLine($"Intervallo (AW): {since:yyyy-MM-dd HH:mm zzz}  →  {untilExclusive:yyyy-MM-dd HH:mm zzz} (fine esclusiva)");

        // ActivityWatch: stessi since/until (fine esclusiva)
        var events = await _aw.GetEventsAsync(windowBucket.Id, since, untilExclusive, ct);
        Console.WriteLine($"Eventi: {events.Count}");

        var normalizedEvents = NormalizeWindowEvents(
            events,
            _opt.Editors.RecognizedApps ?? new List<string>());

        foreach (var ev in normalizedEvents.Take(10))
        {
            var codingTag = ev.IsCoding ? "[coding]" : string.Empty;
            Console.WriteLine($"- {ev.Start:t}-{ev.End:t} {ev.App} {codingTag} | {ev.Title}");
        }

        // Git: estrai commit dal root configurato e ordinali cronologicamente
        var commits = await _gitCommitSource.GetCommitsAsync(
            _opt.Paths.ReposRoot,
            gitStartDate,
            gitEndDateExclusive,
            ct);

        Console.WriteLine($"Commit trovati: {commits.Count}");
        foreach (var group in commits.GroupBy(c => c.RepoName))
        {
            Console.WriteLine($"Repo {group.Key} ({group.Count()} commit)");
            foreach (var commit in group)
                Console.WriteLine($"  [{commit.Timestamp:t}] {commit.Author} {commit.Message} ({commit.Hash[..7]})");
        }

        var associations = CommitAssociator.Associate(
            commits,
            normalizedEvents,
            _opt.Editors.CommitAssociationWindowMinutes);

        Console.WriteLine();
        Console.WriteLine("Associazioni commit ↔ editor:");
        foreach (var assoc in associations)
        {
            if (assoc.EditorEvent is null)
            {
                Console.WriteLine($"  {assoc.Commit.Timestamp:t} {assoc.Commit.RepoName} {assoc.Commit.Message} — nessun editor nel ±{_opt.Editors.CommitAssociationWindowMinutes}m");
                continue;
            }

            Console.WriteLine($"  {assoc.Commit.Timestamp:t} {assoc.Commit.RepoName} {assoc.Commit.Message}");
            Console.WriteLine($"      ↳ {assoc.EditorEvent.Start:t}-{assoc.EditorEvent.End:t} {assoc.EditorEvent.App} | {assoc.EditorEvent.Title}");
        }
    }

    private static (DateTimeOffset since, DateTimeOffset untilExclusive, DateTime gitStartDate, DateTime gitEndDateExclusive)
    GetWindow(WorkHoursOptions wh, ReportWindowOptions? rw)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(wh.TimeZone);

        // Date fisse (se presenti) nel formato yyyy-MM-dd; altrimenti oggi→oggi
        DateOnly startDo, endDo;
        if (rw is not null
            && DateOnly.TryParseExact(rw.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sdo)
            && DateOnly.TryParseExact(rw.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var edo))
        {
            startDo = sdo;
            endDo = edo;
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            startDo = today;
            endDo = today;
        }

        var startTime = TimeOnly.Parse(wh.Start);
        var endTime = TimeOnly.Parse(wh.End);

        // istanti locali
        var startLocal = startDo.ToDateTime(startTime);
        var endLocal = endDo.ToDateTime(endTime);

        // Fine esclusiva: aggiungo 1 minuto così "23:59" include tutto il minuto
        var endLocalExclusive = endLocal.AddMinutes(1);

        var since = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal));
        var untilExclusive = new DateTimeOffset(endLocalExclusive, TimeZoneInfo.Local.GetUtcOffset(endLocalExclusive));

        // Bound per Git helper (solo data; end esclusivo)
        var gitStartDate = startLocal.Date;
        var gitEndDateExclusive = endLocalExclusive.Date;

        return (since, untilExclusive, gitStartDate, gitEndDateExclusive);
    }
    private static List<NormalizedEvent> NormalizeWindowEvents(
        IReadOnlyList<EventDto> awEvents,
        IReadOnlyList<string> recognizedApps)
    {
        if (awEvents.Count == 0)
            return new List<NormalizedEvent>();

        var list = new List<NormalizedEvent>(awEvents.Count);
        foreach (var ev in awEvents)
        {
            var duration = ev.Duration.HasValue
                ? TimeSpan.FromSeconds(ev.Duration.Value)
                : TimeSpan.Zero;
            var end = duration > TimeSpan.Zero
                ? ev.Timestamp + duration
                : ev.Timestamp;

            var app = ev.Data.App ?? string.Empty;
            var isCoding = recognizedApps.Any(rec =>
                !string.IsNullOrWhiteSpace(rec) &&
                app.Contains(rec, StringComparison.OrdinalIgnoreCase));

            list.Add(new NormalizedEvent(
                ev.Timestamp,
                end,
                source: "ActivityWatch",
                kind: "Window",
                app: ev.Data.App,
                title: ev.Data.Title,
                url: ev.Data.Url,
                isCoding: isCoding));
        }

        return list;
    }
}
