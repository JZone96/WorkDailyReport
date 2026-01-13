namespace WorkDailyReport.ETL;

public static class CommitAssociator
{
    public static IReadOnlyList<CommitAssociation> Associate(
        IReadOnlyList<CommitEvent> commits,
        IReadOnlyList<NormalizedEvent> editorEvents,
        int windowMinutes)
    {
        if (commits.Count == 0)
            return Array.Empty<CommitAssociation>();

        var orderedEvents = editorEvents
            .OrderBy(e => e.TsStart)
            .ToList();

        if (orderedEvents.Count == 0)
            return commits
                .Select(c => new CommitAssociation(c, Array.Empty<NormalizedEvent>()))
                .ToList();

        var orderedCoding = orderedEvents
            .Where(e => e.IsCoding)
            .ToList();

        var associations = new List<CommitAssociation>(commits.Count);
        var window = TimeSpan.FromMinutes(windowMinutes <= 0 ? 15 : windowMinutes);
        var usedByRepo = new Dictionary<string, HashSet<NormalizedEvent>>(StringComparer.OrdinalIgnoreCase);

        foreach (var commit in commits)
        {
            var dayStart = StartOfDay(commit.Timestamp);
            var repoKey = commit.RepoName ?? string.Empty;
            if (!usedByRepo.TryGetValue(repoKey, out var usedForRepo))
            {
                usedForRepo = new HashSet<NormalizedEvent>();
                usedByRepo[repoKey] = usedForRepo;
            }

            var matches = orderedEvents
                .Where(ev =>
                    ev.TsStart >= dayStart &&
                    ev.TsStart <= commit.Timestamp &&
                    MatchesRepo(ev, commit.RepoName) &&
                    !usedForRepo.Contains(ev))
                .ToList();

            if (matches.Count == 0)
            {
                var codingAvailable = orderedCoding
                    .Where(ev => !usedForRepo.Contains(ev))
                    .ToList();
                var anyAvailable = orderedEvents
                    .Where(ev => !usedForRepo.Contains(ev))
                    .ToList();

                var fallback = FindBestInWindow(commit, codingAvailable, window)
                    ?? FindBestInWindow(commit, anyAvailable, window);
                if (fallback is not null)
                    matches.Add(fallback);
            }

            foreach (var match in matches.Distinct())
            {
                match.LinkedCommits.Add(commit);
                usedForRepo.Add(match);
            }

            associations.Add(new CommitAssociation(commit, matches));
        }

        return associations;
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset timestamp) =>
        new(timestamp.Year, timestamp.Month, timestamp.Day, 0, 0, 0, timestamp.Offset);

    private static NormalizedEvent? FindBestInWindow(
        CommitEvent commit,
        IReadOnlyList<NormalizedEvent> orderedEditors,
        TimeSpan window)
    {
        if (orderedEditors.Count == 0)
            return null;

        var windowStart = commit.Timestamp - window;
        var windowEnd = commit.Timestamp + window;

        NormalizedEvent? best = null;
        foreach (var editor in orderedEditors)
        {
            if (editor.TsEnd < windowStart)
                continue;
            if (editor.TsStart > windowEnd)
                break;

            best ??= editor;

            if (MatchesRepo(editor, commit.RepoName))
                return editor;
        }

        return best;
    }

    private static bool MatchesRepo(NormalizedEvent editor, string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName))
            return true;

        if (!string.IsNullOrWhiteSpace(editor.ProjectTag) &&
            editor.ProjectTag.Contains(repoName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(editor.Title) &&
            editor.Title.Contains(repoName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(editor.Url) &&
            editor.Url.Contains(repoName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

public sealed record CommitAssociation(CommitEvent Commit, IReadOnlyList<NormalizedEvent> EditorEvents);
