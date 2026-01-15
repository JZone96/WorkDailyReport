using Microsoft.Extensions.Options;
using WorkDailyReport.ActivityWatch;
using WorkDailyReport.Config;
using WorkDailyReport.utils;
using System.Globalization;
using System.Linq;
using WorkDailyReport.Calendar;
using System.Text.RegularExpressions;

namespace WorkDailyReport.ETL;

public sealed class DailyRunner
{
    private readonly IActivityWatchClient _aw;
    private readonly IGitCommitSource _gitCommitSource;
    private readonly ICalendarEventSource _calendarSource;
    private readonly WorkReportOptions _opt;

    public DailyRunner(
        IActivityWatchClient aw,
        IGitCommitSource gitCommitSource,
        ICalendarEventSource calendarSource,
        IOptions<WorkReportOptions> opt)
    {
        _aw = aw;
        _gitCommitSource = gitCommitSource;
        _calendarSource = calendarSource;
        _opt = opt.Value;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var (since, untilExclusive, gitSince, gitUntil, startDateOnly, endDateOnly) =
            GetWindow(_opt.WorkHours, _opt.ReportWindow);
        var workIntervals = BuildWorkIntervals(_opt.WorkHours, startDateOnly, endDateOnly);
        var dataTimeZone = TimeZoneInfo.FindSystemTimeZoneById(_opt.WorkHours.TimeZone);

        var buckets = await _aw.ListBucketsAsync(ct);
        if (buckets.Count == 0)
        {
            Console.WriteLine("⚠ Nessun bucket. Avvia ActivityWatch.");
            return;
        }

        var preferredWatchers = _opt.ActivityWatch.Watchers ?? new List<string>();
        var windowWatcherIds = preferredWatchers
            .Where(w => !w.Contains("afk", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (windowWatcherIds.Count == 0)
            windowWatcherIds.AddRange(new[] { "aw-watcher-window", "aw-watcher-web" });

        var windowBucket = FindBucketByPriority(buckets, windowWatcherIds)
            ?? FindBucketByPriority(buckets, new List<string> { "aw-watcher-window", "aw-watcher-web" });
        var afkBucket = FindBucketByPriority(buckets, new List<string> { "aw-watcher-afk" });

        if (windowBucket is null)
        {
            Console.WriteLine("⚠ Nessun bucket window.");
            return;
        }

        Console.WriteLine($"Intervallo (AW): {since:yyyy-MM-dd HH:mm zzz}  →  {untilExclusive:yyyy-MM-dd HH:mm zzz} (fine esclusiva)");
        Console.WriteLine($"Bucket window: {windowBucket.Id}");
        Console.WriteLine($"Bucket AFK: {(afkBucket is null ? "n/a" : afkBucket.Id)}");

        // ActivityWatch: stessi since/until (fine esclusiva)
        var events = await _aw.GetEventsAsync(windowBucket.Id, since, untilExclusive, ct);
        Console.WriteLine($"Eventi: {events.Count}");

        var normalizedEvents = NormalizeWindowEvents(
            events,
            _opt.Editors.RecognizedApps ?? new List<string>(),
            dataTimeZone);

        foreach (var ev in normalizedEvents)
        {
            var localStart = AdjustToTimeZone(ev.TsStart, dataTimeZone);
            var localEnd = AdjustToTimeZone(ev.TsEnd, dataTimeZone);
            var codingTag = ev.IsCoding ? "[coding]" : string.Empty;
            Console.WriteLine($"- {localStart:t}-{localEnd:t} {ev.App} {codingTag} | {ev.Title}");
        }

        var normalizedAfkEvents = new List<NormalizedEvent>();
        if (afkBucket is not null)
        {
            var afkRaw = await _aw.GetEventsAsync(afkBucket.Id, since, untilExclusive, ct);
            normalizedAfkEvents = NormalizeAfkEvents(afkRaw, dataTimeZone);
            normalizedAfkEvents = MergeAfkEvents(normalizedAfkEvents, _opt.Filters.MergeGapSeconds);
            Console.WriteLine($"Eventi AFK: {normalizedAfkEvents.Count}");
            foreach (var afk in normalizedAfkEvents)
            {
                Console.WriteLine($"- AFK {afk.TsStart:t}-{afk.TsEnd:t} ({afk.Duration.TotalMinutes:F0} min)");
            }
        }
        else
        {
            Console.WriteLine("⚠ Nessun bucket AFK trovato (nessuna separazione AFK).");
        }

        // Git: estrai commit dal root configurato e ordinali cronologicamente
        var commits = await _gitCommitSource.GetCommitsAsync(
            _opt.Paths.ReposRoot,
            gitSince,
            gitUntil,
            ct);

        Console.WriteLine($"Commit trovati: {commits.Count}");
        foreach (var group in commits.GroupBy(c => c.RepoName))
        {
            Console.WriteLine($"Repo {group.Key} ({group.Count()} commit)");
            foreach (var commit in group)
            {
                var commitLocal = AdjustToTimeZone(commit.Timestamp, dataTimeZone);
                Console.WriteLine($"  [{commitLocal:t}] {commit.Author} {commit.Message} ({commit.Hash[..7]})");
            }
        }

        var calendarEvents = await _calendarSource.GetEventsAsync(since, untilExclusive, ct);
        Console.WriteLine($"Eventi calendario Outlook: {calendarEvents.Count}");
        foreach (var cal in calendarEvents)
        {
            var calStartLocal = AdjustToTimeZone(cal.Start, dataTimeZone);
            var calEndLocal = AdjustToTimeZone(cal.End, dataTimeZone);
            Console.WriteLine($"- {calStartLocal:yyyy-MM-dd HH:mm} → {calEndLocal:HH:mm} {cal.Title} @ {cal.Location}");
        }
        var normalizedCalendarEvents = calendarEvents
            .Select(cal => new NormalizedEvent(
                cal.Start,
                cal.End,
                source: "Outlook",
                kind: "Calendar",
                app: "Outlook",
                title: cal.Title,
                url: cal.Location,
                isCoding: false))
            .ToList();
        normalizedEvents.AddRange(normalizedCalendarEvents);

        if (_opt.Filters.ExcludeAFK && normalizedAfkEvents.Count > 0)
        {
            normalizedEvents = RemoveAfkOverlap(normalizedEvents, normalizedAfkEvents);
        }
        normalizedEvents.AddRange(normalizedAfkEvents);

        normalizedEvents = ClipEventsToWorkSchedule(normalizedEvents, workIntervals);
        normalizedEvents = FilterByDuration(normalizedEvents, _opt.Filters.MinDurationSeconds);
        normalizedEvents = MergeEvents(normalizedEvents, _opt.Filters.MergeGapSeconds);
        normalizedEvents.AddRange(BuildScheduleReminders(_opt.WorkHours, startDateOnly, endDateOnly));

        var focusBlocks = BuildFocusBlocks(normalizedEvents);
        var focusBuffer = TimeSpan.FromMinutes(2);
        TagEventsWithFocusBlocks(normalizedEvents, focusBlocks, focusBuffer);

        var projectTitles = BuildProjectTitleIndex(_opt.Paths.ReposRoot);
        if (projectTitles.Count > 0)
        {
            TagBrowserEventsByTitle(normalizedEvents, projectTitles);
            var browserGapMinutes = _opt.Editors.CommitAssociationWindowMinutes <= 0
                ? 15
                : _opt.Editors.CommitAssociationWindowMinutes;
            var browserBlocks = BuildBrowserProjectBlocks(
                normalizedEvents,
                projectTitles,
                TimeSpan.FromMinutes(browserGapMinutes));
            TagEventsWithProjectBlocks(normalizedEvents, browserBlocks);
        }

        Console.WriteLine();
        if (focusBlocks.Count == 0)
        {
            Console.WriteLine("Blocchi focus IDE: nessuno rilevato.");
        }
        else
        {
            Console.WriteLine("Blocchi focus guidati dall'IDE:");
            foreach (var block in focusBlocks)
            {
                var relatedCount = CountEventsForBlock(normalizedEvents, block, focusBuffer);
                var blockStartLocal = AdjustToTimeZone(block.Start, dataTimeZone);
                var blockEndLocal = AdjustToTimeZone(block.End, dataTimeZone);

                Console.WriteLine(
                    $"- {blockStartLocal:HH:mm}-{blockEndLocal:HH:mm} {block.Label} ({block.Duration.TotalMinutes:F0} min, {relatedCount} eventi)");
            }

            Console.WriteLine();
            Console.WriteLine("Racconto blocchi IDE:");
            foreach (var block in focusBlocks)
            {
                var blockEvents = GetEventsForBlock(normalizedEvents, block, focusBuffer);
                var primaryApp = blockEvents
                    .Where(e => !string.IsNullOrWhiteSpace(e.App))
                    .GroupBy(e => e.App!)
                    .Select(g => new { App = g.Key, Minutes = g.Sum(ev => ev.Duration.TotalMinutes) })
                    .OrderByDescending(x => x.Minutes)
                    .FirstOrDefault()?.App ?? "l'IDE";

                var sampleTitles = blockEvents
                    .Where(e => !string.IsNullOrWhiteSpace(e.Title))
                    .Select(e => e.Title!.Trim())
                    .Distinct()
                    .Take(2)
                    .ToList();

                var commitsInBlock = commits.Count(c => c.Timestamp >= block.Start && c.Timestamp <= block.End);
                var commitText = commitsInBlock > 0 ? $" e registrato {commitsInBlock} commit" : string.Empty;
                var titleText = sampleTitles.Count switch
                {
                    0 => string.Empty,
                    1 => $" (es. {sampleTitles[0]})",
                    _ => $" (es. {sampleTitles[0]}, {sampleTitles[1]})"
                };

                var blockStartLocal = AdjustToTimeZone(block.Start, dataTimeZone);
                var blockEndLocal = AdjustToTimeZone(block.End, dataTimeZone);

                Console.WriteLine(
                    $"  • {block.Label}: {blockStartLocal:HH:mm}-{blockEndLocal:HH:mm} (~{block.Duration.TotalMinutes:F0}m) in {primaryApp}{titleText}{commitText}.");
            }
        }

        var uniformRows = BuildUniformRows(normalizedEvents, commits);
        Console.WriteLine();
        Console.WriteLine("Eventi normalizzati (schema ts_start/ts_end/duration/app/title/url):");
        foreach (var row in uniformRows)
        {
            var startLocal = AdjustToTimeZone(row.TsStart, dataTimeZone);
            var endLocal = AdjustToTimeZone(row.TsEnd, dataTimeZone);
            Console.WriteLine(
                $"- {startLocal:yyyy-MM-dd HH:mm} → {endLocal:HH:mm} | {row.App} | {row.Title} | dur={row.DurationSeconds:F0}s | {row.Source}/{row.Kind}{FormatProjectTag(row.ProjectTag)}");
        }

        var associations = CommitAssociator.Associate(
            commits,
            normalizedEvents,
            _opt.Editors.CommitAssociationWindowMinutes);

        Console.WriteLine();
        Console.WriteLine("Associazioni commit ↔ editor:");
        foreach (var assoc in associations)
        {
            var commitLocal = AdjustToTimeZone(assoc.Commit.Timestamp, dataTimeZone);
            if (assoc.EditorEvents.Count == 0)
            {
                Console.WriteLine($"  {commitLocal:t} {assoc.Commit.RepoName} {assoc.Commit.Message} — nessun editor rilevato nella giornata (fallback ±{_opt.Editors.CommitAssociationWindowMinutes}m)");
                continue;
            }

            Console.WriteLine($"  {commitLocal:t} {assoc.Commit.RepoName} {assoc.Commit.Message}");
            foreach (var editor in assoc.EditorEvents)
            {
                var editorStart = AdjustToTimeZone(editor.TsStart, dataTimeZone);
                var editorEnd = AdjustToTimeZone(editor.TsEnd, dataTimeZone);
                Console.WriteLine($"      ↳ {editorStart:t}-{editorEnd:t} {editor.App} | {editor.Title}");
            }
            var totalMinutes = assoc.EditorEvents.Sum(e => e.Duration.TotalMinutes);
            var spanStart = assoc.EditorEvents.Min(e => e.TsStart);
            var spanEnd = assoc.EditorEvents.Max(e => e.TsEnd);
            var spanMinutes = (spanEnd - spanStart).TotalMinutes;
            Console.WriteLine($"      Totale: {FormatMinutes(totalMinutes)} ({Math.Round(totalMinutes):F0} min)");
            Console.WriteLine($"      Finestra: {FormatMinutes(spanMinutes)} ({Math.Round(spanMinutes):F0} min)");
        }

    }

    private static (DateTimeOffset since, DateTimeOffset untilExclusive, DateTimeOffset gitSince, DateTimeOffset gitUntil, DateOnly startDate, DateOnly endDate)
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

        var since = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal));
        var untilExclusive = new DateTimeOffset(endLocalExclusive, tz.GetUtcOffset(endLocalExclusive));

        // Bound Git: copri sempre l'intera giornata (00:00 → 23:59:59) nel fuso specificato
        var gitSinceLocal = startDo.ToDateTime(TimeOnly.MinValue);
        var gitSince = new DateTimeOffset(gitSinceLocal, tz.GetUtcOffset(gitSinceLocal));

        var gitUntilLocal = endDo.ToDateTime(new TimeOnly(23, 59, 59));
        var gitUntil = new DateTimeOffset(gitUntilLocal, tz.GetUtcOffset(gitUntilLocal));

        return (since, untilExclusive, gitSince, gitUntil, startDo, endDo);
    }

    private static string FormatMinutes(double minutes)
    {
        var rounded = Math.Max(0, (int)Math.Round(minutes));
        var ts = TimeSpan.FromMinutes(rounded);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }

    private static BucketDto? FindBucketByPriority(
        IReadOnlyList<BucketDto> buckets,
        IReadOnlyList<string> watcherIds)
    {
        foreach (var watcherId in watcherIds)
        {
            if (string.IsNullOrWhiteSpace(watcherId))
                continue;

            var match = buckets.FirstOrDefault(b =>
                b.Id.Contains(watcherId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }
    private static List<NormalizedEvent> NormalizeWindowEvents(
        IReadOnlyList<EventDto> awEvents,
        IReadOnlyList<string> recognizedApps,
        TimeZoneInfo tz)
    {
        if (awEvents.Count == 0)
            return new List<NormalizedEvent>();

        var list = new List<NormalizedEvent>(awEvents.Count);
        foreach (var ev in awEvents)
        {
            var start = AdjustToTimeZone(ev.Timestamp, tz);
            var duration = ev.Duration.HasValue
                ? TimeSpan.FromSeconds(ev.Duration.Value)
                : TimeSpan.Zero;
            var endInstant = duration > TimeSpan.Zero
                ? ev.Timestamp + duration
                : ev.Timestamp;
            var end = AdjustToTimeZone(endInstant, tz);

            var app = ev.Data.App ?? string.Empty;
            var isCoding = recognizedApps.Any(rec =>
                !string.IsNullOrWhiteSpace(rec) &&
                app.Contains(rec, StringComparison.OrdinalIgnoreCase));

            list.Add(new NormalizedEvent(
                start,
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

    private static List<NormalizedEvent> NormalizeAfkEvents(
        IReadOnlyList<EventDto> afkEvents,
        TimeZoneInfo tz)
    {
        if (afkEvents.Count == 0)
            return new List<NormalizedEvent>();

        var list = new List<NormalizedEvent>();
        foreach (var ev in afkEvents)
        {
            var status = ev.Data.Status;
            if (!string.Equals(status, "afk", StringComparison.OrdinalIgnoreCase))
                continue;

            var start = AdjustToTimeZone(ev.Timestamp, tz);
            var duration = ev.Duration.HasValue
                ? TimeSpan.FromSeconds(ev.Duration.Value)
                : TimeSpan.Zero;
            var endInstant = duration > TimeSpan.Zero
                ? ev.Timestamp + duration
                : ev.Timestamp;
            var end = AdjustToTimeZone(endInstant, tz);

            list.Add(new NormalizedEvent(
                start,
                end,
                source: "ActivityWatch",
                kind: "AFK",
                app: "AFK",
                title: "Assente",
                url: null,
                isCoding: false));
        }

        return list;
    }

    private static List<NormalizedEvent> MergeAfkEvents(
        List<NormalizedEvent> afkEvents,
        int mergeGapSeconds)
    {
        if (afkEvents.Count <= 1)
            return afkEvents
                .OrderBy(e => e.TsStart)
                .ToList();

        var ordered = afkEvents
            .OrderBy(e => e.TsStart)
            .ToList();

        var merged = new List<NormalizedEvent>(ordered.Count);
        var maxGap = TimeSpan.FromSeconds(Math.Max(0, mergeGapSeconds));

        var currentStart = ordered[0].TsStart;
        var currentEnd = ordered[0].TsEnd;
        var template = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            var gap = candidate.TsStart - currentEnd;
            if (gap < TimeSpan.Zero)
                gap = TimeSpan.Zero;

            if (gap <= maxGap)
            {
                if (candidate.TsEnd > currentEnd)
                    currentEnd = candidate.TsEnd;
            }
            else
            {
                merged.Add(new NormalizedEvent(
                    currentStart,
                    currentEnd,
                    template.Source,
                    template.Kind,
                    template.App,
                    template.Title,
                    template.Url,
                    template.IsCoding));

                template = candidate;
                currentStart = candidate.TsStart;
                currentEnd = candidate.TsEnd;
            }
        }

        merged.Add(new NormalizedEvent(
            currentStart,
            currentEnd,
            template.Source,
            template.Kind,
            template.App,
            template.Title,
            template.Url,
            template.IsCoding));

        return merged;
    }

    private static string FormatProjectTag(string tag) =>
        string.IsNullOrWhiteSpace(tag) ? string.Empty : $" [proj: {tag}]";

    private static List<NormalizedRow> BuildUniformRows(
        List<NormalizedEvent> normalizedEvents,
        IReadOnlyList<CommitEvent> commits)
    {
        var rows = new List<NormalizedRow>(normalizedEvents.Count + commits.Count);
        rows.AddRange(normalizedEvents.Select(e => e.ToRow()));

        foreach (var commit in commits)
        {
            rows.Add(new NormalizedRow(
                commit.Timestamp,
                commit.Timestamp,
                0,
                commit.RepoName,
                commit.Message,
                commit.Hash,
                "Git",
                "Commit",
                string.Empty));
        }

        return rows
            .OrderBy(r => r.TsStart)
            .ToList();
    }

    private static int CountEventsForBlock(
        List<NormalizedEvent> events,
        FocusBlock block,
        TimeSpan includeBuffer) =>
        GetEventsForBlock(events, block, includeBuffer).Count;

    private static List<NormalizedEvent> GetEventsForBlock(
        List<NormalizedEvent> events,
        FocusBlock block,
        TimeSpan includeBuffer)
    {
        var blockStart = block.Start - includeBuffer;
        var blockEnd = block.End + includeBuffer;

        return events
            .Where(e => e.ProjectTag == block.Label &&
                        e.TsEnd > blockStart &&
                        e.TsStart < blockEnd)
            .ToList();
    }

    private static List<WorkInterval> BuildWorkIntervals(
        WorkHoursOptions options,
        DateOnly startDate,
        DateOnly endDate)
    {
        var intervals = new List<WorkInterval>();
        if (startDate > endDate)
            return intervals;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var workDays = new HashSet<DayOfWeek>(options.WorkDays ?? new List<DayOfWeek>());

        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            if (!workDays.Contains(current.DayOfWeek))
                continue;

            var schedule = ResolveScheduleForDay(options, current.DayOfWeek);
            if (schedule is null)
                continue;

            AddIntervals(intervals, current, schedule, tz);
        }

        return intervals;
    }

    private static ResolvedWorkSchedule? ResolveScheduleForDay(WorkHoursOptions options, DayOfWeek day)
    {
        options.DailyOverrides ??= new Dictionary<string, DailyWorkHoursOverride>();
        options.DailyOverrides.TryGetValue(day.ToString(), out var daily);

        var start = ParseTimeOnly(daily?.Start ?? options.Start);
        var end = ParseTimeOnly(daily?.End ?? options.End);
        if (start >= end)
            return null;

        var lunch = daily?.LunchBreak ?? options.LunchBreak;
        TimeOnly? lunchStart = null;
        TimeOnly? lunchEnd = null;
        if (lunch is not null)
        {
            lunchStart = ParseTimeOnly(lunch.Start);
            lunchEnd = ParseTimeOnly(lunch.End);
            if (lunchStart >= lunchEnd)
            {
                lunchStart = null;
                lunchEnd = null;
            }
        }

        return new ResolvedWorkSchedule(start, end, lunchStart, lunchEnd);
    }

    private static TimeOnly ParseTimeOnly(string value) =>
        TimeOnly.TryParse(value, out var parsed) ? parsed : TimeOnly.MinValue;

    private static void AddIntervals(
        List<WorkInterval> intervals,
        DateOnly day,
        ResolvedWorkSchedule schedule,
        TimeZoneInfo tz)
    {
        var dayStart = day.ToDateTime(schedule.WorkStart);
        var dayEnd = day.ToDateTime(schedule.WorkEnd);

        if (schedule.LunchStart is TimeOnly lunchStart && schedule.LunchEnd is TimeOnly lunchEnd)
        {
            var lunchStartDt = day.ToDateTime(lunchStart);
            var lunchEndDt = day.ToDateTime(lunchEnd);

            if (lunchStartDt > dayStart)
                intervals.Add(new WorkInterval(ToOffset(dayStart, tz), ToOffset(lunchStartDt, tz)));

            if (dayEnd > lunchEndDt)
                intervals.Add(new WorkInterval(ToOffset(lunchEndDt, tz), ToOffset(dayEnd, tz)));
        }
        else
        {
            intervals.Add(new WorkInterval(ToOffset(dayStart, tz), ToOffset(dayEnd, tz)));
        }
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }

    private static List<NormalizedEvent> ClipEventsToWorkSchedule(
        List<NormalizedEvent> events,
        IReadOnlyList<WorkInterval> intervals)
    {
        if (intervals.Count == 0)
            return events;

        var clipped = new List<NormalizedEvent>();
        foreach (var ev in events)
        {
            foreach (var interval in intervals)
            {
                var start = ev.TsStart > interval.Start ? ev.TsStart : interval.Start;
                var end = ev.TsEnd < interval.End ? ev.TsEnd : interval.End;

                if (end <= start)
                    continue;

                clipped.Add(new NormalizedEvent(
                    start,
                    end,
                    ev.Source,
                    ev.Kind,
                    ev.App,
                    ev.Title,
                    ev.Url,
                    ev.IsCoding));
            }
        }

        return clipped;
    }

    private static List<NormalizedEvent> FilterByDuration(
        List<NormalizedEvent> events,
        int minDurationSeconds)
    {
        if (minDurationSeconds <= 0)
            return events;

        var cutoff = TimeSpan.FromSeconds(minDurationSeconds);
        return events
            .Where(ev =>
                ev.Kind.Equals("Calendar", StringComparison.OrdinalIgnoreCase) ||
                ev.Kind.Equals("AFK", StringComparison.OrdinalIgnoreCase) ||
                ev.Duration >= cutoff)
            .ToList();
    }

    private static List<NormalizedEvent> BuildScheduleReminders(
        WorkHoursOptions options,
        DateOnly startDate,
        DateOnly endDate)
    {
        var reminders = new List<NormalizedEvent>();
        if (startDate > endDate)
            return reminders;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var workDays = new HashSet<DayOfWeek>(options.WorkDays ?? new List<DayOfWeek>());

        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            if (!workDays.Contains(current.DayOfWeek))
                continue;

            var schedule = ResolveScheduleForDay(options, current.DayOfWeek);
            if (schedule is null)
                continue;

            AddReminder(reminders, current, schedule.WorkStart, tz, "Promemoria: tra 5 minuti inizia il lavoro");
            if (schedule.LunchStart is not null)
                AddReminder(reminders, current, schedule.LunchStart.Value, tz, "Promemoria: tra 5 minuti inizia la pausa pranzo");
            if (schedule.LunchEnd is not null)
                AddReminder(reminders, current, schedule.LunchEnd.Value, tz, "Promemoria: tra 5 minuti finisce la pausa pranzo");
            AddReminder(reminders, current, schedule.WorkEnd, tz, "Promemoria: tra 5 minuti finisce il lavoro");
        }

        return reminders;
    }

    private static List<NormalizedEvent> RemoveAfkOverlap(
        List<NormalizedEvent> events,
        IReadOnlyList<NormalizedEvent> afkEvents)
    {
        var afkIntervals = afkEvents
            .Where(afk => afk.Duration > TimeSpan.Zero)
            .Select(afk => (Start: afk.TsStart, End: afk.TsEnd))
            .OrderBy(interval => interval.Start)
            .ToList();

        if (afkIntervals.Count == 0)
            return events;

        var cleaned = new List<NormalizedEvent>();

        foreach (var ev in events)
        {
            var segments = new List<(DateTimeOffset Start, DateTimeOffset End)>
            {
                (ev.TsStart, ev.TsEnd)
            };

            foreach (var afk in afkIntervals)
            {
                if (segments.Count == 0)
                    break;

                var splitted = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                foreach (var seg in segments)
                {
                    splitted.AddRange(SubtractInterval(seg, afk));
                }

                segments = splitted;
            }

            foreach (var seg in segments)
            {
                if (seg.End <= seg.Start)
                    continue;

                cleaned.Add(new NormalizedEvent(
                    seg.Start,
                    seg.End,
                    ev.Source,
                    ev.Kind,
                    ev.App,
                    ev.Title,
                    ev.Url,
                    ev.IsCoding));
            }
        }

        return cleaned;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SubtractInterval(
        (DateTimeOffset Start, DateTimeOffset End) segment,
        (DateTimeOffset Start, DateTimeOffset End) block)
    {
        if (block.End <= segment.Start || block.Start >= segment.End)
        {
            yield return segment;
            yield break;
        }

        if (block.Start > segment.Start)
            yield return (segment.Start, block.Start);

        if (block.End < segment.End)
            yield return (block.End, segment.End);
    }

    private static List<NormalizedEvent> MergeEvents(
        List<NormalizedEvent> events,
        int mergeGapSeconds)
    {
        if (events.Count <= 1 || mergeGapSeconds < 0)
            return events
                .OrderBy(e => e.TsStart)
                .ToList();

        var ordered = events
            .OrderBy(e => e.TsStart)
            .ToList();
        var merged = new List<NormalizedEvent>(ordered.Count);
        var maxGap = TimeSpan.FromSeconds(mergeGapSeconds);
        var current = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            if (CanMerge(current, candidate, maxGap))
            {
                current = MergePair(current, candidate);
            }
            else
            {
                merged.Add(current);
                current = candidate;
            }
        }

        merged.Add(current);
        return merged;
    }

    private static bool CanMerge(
        NormalizedEvent first,
        NormalizedEvent second,
        TimeSpan maxGap)
    {
        if (!string.Equals(first.Source, second.Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(first.Kind, second.Kind, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(first.App, second.App, StringComparison.Ordinal) ||
            !string.Equals(first.Title, second.Title, StringComparison.Ordinal) ||
            !string.Equals(first.Url, second.Url, StringComparison.Ordinal) ||
            first.IsCoding != second.IsCoding)
        {
            return false;
        }

        var gap = second.TsStart - first.TsEnd;
        if (gap < TimeSpan.Zero)
            gap = TimeSpan.Zero;

        return gap <= maxGap;
    }

    private static NormalizedEvent MergePair(NormalizedEvent first, NormalizedEvent second)
    {
        var start = first.TsStart <= second.TsStart ? first.TsStart : second.TsStart;
        var end = first.TsEnd >= second.TsEnd ? first.TsEnd : second.TsEnd;

        return new NormalizedEvent(
            start,
            end,
            first.Source,
            first.Kind,
            first.App,
            first.Title,
            first.Url,
            first.IsCoding);
    }

    private static void AddReminder(
        List<NormalizedEvent> reminders,
        DateOnly day,
        TimeOnly target,
        TimeZoneInfo tz,
        string title)
    {
        var reminderInstant = day.ToDateTime(target).AddMinutes(-5);
        var reminderOffset = ToOffset(reminderInstant, tz);
        reminders.Add(new NormalizedEvent(
            reminderOffset,
            reminderOffset,
            source: "Scheduler",
            kind: "Reminder",
            app: "Scheduler",
            title: title,
            url: null,
            isCoding: false));
    }

    private static DateTimeOffset AdjustToTimeZone(DateTimeOffset timestamp, TimeZoneInfo tz)
    {
        var converted = TimeZoneInfo.ConvertTime(timestamp, tz);
        var targetOffset = tz.GetUtcOffset(converted.DateTime);
        if ((converted.Offset - targetOffset).Duration() > TimeSpan.FromSeconds(1))
        {
            var localClock = DateTime.SpecifyKind(converted.DateTime, DateTimeKind.Unspecified);
            return new DateTimeOffset(localClock, targetOffset);
        }

        return converted;
    }

    private static Dictionary<string, string> BuildProjectTitleIndex(string reposRoot)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(reposRoot) || !Directory.Exists(reposRoot))
            return titles;

        foreach (var dir in Directory.EnumerateDirectories(reposRoot))
        {
            var repoName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(repoName))
                continue;

            var indexPath = Path.Combine(dir, "index.html");
            if (!File.Exists(indexPath))
                continue;

            var title = TryReadHtmlTitle(indexPath);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            titles[repoName] = title;
        }

        return titles;
    }

    private static string? TryReadHtmlTitle(string path)
    {
        try
        {
            var html = File.ReadAllText(path);
            return ExtractHtmlTitle(html);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractHtmlTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var match = Regex.Match(
            html,
            "<title[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
            return null;

        var title = Regex.Replace(match.Groups[1].Value, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static void TagBrowserEventsByTitle(
        List<NormalizedEvent> events,
        IReadOnlyDictionary<string, string> projectTitles)
    {
        foreach (var ev in events)
        {
            if (ev.ProjectTag is not null || !IsBrowserApp(ev.App) || string.IsNullOrWhiteSpace(ev.Title))
                continue;

            var match = MatchProjectByTitle(ev.Title, projectTitles);
            if (!string.IsNullOrWhiteSpace(match))
                ev.AssignProject(match);
        }
    }

    private static List<ProjectBlock> BuildBrowserProjectBlocks(
        List<NormalizedEvent> events,
        IReadOnlyDictionary<string, string> projectTitles,
        TimeSpan maxGap)
    {
        var hits = events
            .Where(e => IsBrowserApp(e.App) && !string.IsNullOrWhiteSpace(e.Title))
            .Select(e => (Event: e, Project: MatchProjectByTitle(e.Title!, projectTitles)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Project))
            .ToList();

        foreach (var hit in hits)
        {
            hit.Event.AssignProject(hit.Project);
        }

        return hits
            .GroupBy(x => x.Project!, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => BuildBlocksForProject(group.Key, group.Select(x => x.Event), maxGap))
            .ToList();
    }

    private static IEnumerable<ProjectBlock> BuildBlocksForProject(
        string project,
        IEnumerable<NormalizedEvent> events,
        TimeSpan maxGap)
    {
        var ordered = events
            .OrderBy(e => e.TsStart)
            .ToList();
        if (ordered.Count == 0)
            return Enumerable.Empty<ProjectBlock>();

        var blocks = new List<ProjectBlock>();
        var currentStart = ordered[0].TsStart;
        var currentEnd = ordered[0].TsEnd;

        foreach (var ev in ordered.Skip(1))
        {
            var gap = ev.TsStart - currentEnd;
            if (gap < TimeSpan.Zero)
                gap = TimeSpan.Zero;

            if (gap <= maxGap)
            {
                if (ev.TsEnd > currentEnd)
                    currentEnd = ev.TsEnd;
            }
            else
            {
                blocks.Add(new ProjectBlock(project, currentStart, currentEnd));
                currentStart = ev.TsStart;
                currentEnd = ev.TsEnd;
            }
        }

        blocks.Add(new ProjectBlock(project, currentStart, currentEnd));
        return blocks;
    }

    private static void TagEventsWithProjectBlocks(
        List<NormalizedEvent> events,
        IReadOnlyList<ProjectBlock> blocks)
    {
        if (blocks.Count == 0)
            return;

        foreach (var block in blocks)
        {
            foreach (var ev in events)
            {
                if (ev.ProjectTag is not null)
                    continue;

                if (ev.TsEnd <= block.Start || ev.TsStart >= block.End)
                    continue;

                ev.AssignProject(block.Project);
            }
        }
    }

    private static string? MatchProjectByTitle(
        string title,
        IReadOnlyDictionary<string, string> projectTitles)
    {
        string? match = null;
        var matchLen = 0;
        foreach (var kvp in projectTitles)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value))
                continue;

            if (title.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase) &&
                kvp.Value.Length > matchLen)
            {
                match = kvp.Key;
                matchLen = kvp.Value.Length;
            }
        }

        return match;
    }

    private static bool IsBrowserApp(string? app)
    {
        if (string.IsNullOrWhiteSpace(app))
            return false;

        return app.Contains("chrome", StringComparison.OrdinalIgnoreCase)
            || app.Contains("msedge", StringComparison.OrdinalIgnoreCase)
            || app.Contains("edge", StringComparison.OrdinalIgnoreCase)
            || app.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            || app.Contains("brave", StringComparison.OrdinalIgnoreCase)
            || app.Contains("opera", StringComparison.OrdinalIgnoreCase);
    }

    private static List<FocusBlock> BuildFocusBlocks(List<NormalizedEvent> events)
    {
        var coding = events
            .Where(e => e.IsCoding)
            .OrderBy(e => e.TsStart)
            .ToList();

        var result = new List<FocusBlock>();
        if (coding.Count == 0)
            return result;

        var maxGap = TimeSpan.FromMinutes(5);
        FocusBlock? current = null;

        foreach (var ev in coding)
        {
            if (current is null)
            {
                current = new FocusBlock(ev.TsStart, ev.TsEnd, ExtractProjectLabel(ev));
                continue;
            }

            if (ev.TsStart - current.End <= maxGap)
            {
                current.Extend(ev.TsEnd);
                current.TryUpdateLabel(ExtractProjectLabel(ev));
            }
            else
            {
                result.Add(current);
                current = new FocusBlock(ev.TsStart, ev.TsEnd, ExtractProjectLabel(ev));
            }
        }

        if (current is not null)
            result.Add(current);

        return result;
    }

    private static void TagEventsWithFocusBlocks(
        List<NormalizedEvent> events,
        IReadOnlyList<FocusBlock> blocks,
        TimeSpan includeBuffer)
    {
        if (blocks.Count == 0)
            return;

        foreach (var block in blocks)
        {
            var blockStart = block.Start - includeBuffer;
            var blockEnd = block.End + includeBuffer;

            foreach (var ev in events)
            {
                if (ev.ProjectTag is not null)
                    continue;

                if (ev.TsEnd <= blockStart || ev.TsStart >= blockEnd)
                    continue;

                ev.AssignProject(block.Label);
            }
        }
    }

    private static string ExtractProjectLabel(NormalizedEvent ev)
    {
        if (!string.IsNullOrWhiteSpace(ev.Title))
        {
            var segments = ev.Title.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments.Reverse())
            {
                if (segment.Contains("visual studio", StringComparison.OrdinalIgnoreCase)
                    || segment.Contains("visual studio code", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals(ev.App, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (segment.Length <= 2)
                    continue;

                return segment;
            }

            return segments.FirstOrDefault() ?? ev.Title;
        }

        return ev.App ?? "Coding";
    }


    private sealed record WorkInterval(DateTimeOffset Start, DateTimeOffset End);

    private sealed record ResolvedWorkSchedule(
        TimeOnly WorkStart,
        TimeOnly WorkEnd,
        TimeOnly? LunchStart,
        TimeOnly? LunchEnd);

    private sealed record ProjectBlock(string Project, DateTimeOffset Start, DateTimeOffset End);

    private sealed class FocusBlock
    {
        private const string DefaultLabel = "Coding";

        public FocusBlock(DateTimeOffset start, DateTimeOffset end, string label)
        {
            Start = start;
            End = end;
            Label = string.IsNullOrWhiteSpace(label) ? DefaultLabel : label;
        }

        public DateTimeOffset Start { get; private set; }
        public DateTimeOffset End { get; private set; }
        public string Label { get; private set; }
        public TimeSpan Duration => End - Start;

        public void Extend(DateTimeOffset candidateEnd)
        {
            if (candidateEnd > End)
                End = candidateEnd;
        }

        public void TryUpdateLabel(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            if (Label == DefaultLabel)
                Label = candidate;
        }
    }
}
