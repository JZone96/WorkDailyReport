namespace WorkDailyReport.ETL;

public static class CommitAssociator
{
    public static IReadOnlyList<CommitAssociation> Associate(
        IReadOnlyList<CommitEvent> commits,
        IReadOnlyList<NormalizedEvent> editorEvents,
        int windowMinutes)
    {
        if (commits.Count == 0 || editorEvents.Count == 0)
            return commits.Select(c => new CommitAssociation(c, null)).ToList();

        var orderedEditors = editorEvents.OrderBy(e => e.TsStart).ToList();
        var associations = new List<CommitAssociation>(commits.Count);
        var window = TimeSpan.FromMinutes(windowMinutes <= 0 ? 15 : windowMinutes);

        foreach (var commit in commits)
        {
            var windowStart = commit.Timestamp - window;
            var windowEnd = commit.Timestamp + window;

            NormalizedEvent? best = null;
            foreach (var editor in orderedEditors)
            {
                if (editor.TsEnd < windowStart)
                    continue;
                if (editor.TsStart > windowEnd)
                    break;

                if (!editor.IsCoding)
                    continue;

                best ??= editor;

                if (!string.IsNullOrWhiteSpace(commit.RepoName)
                    && (editor.Title?.Contains(commit.RepoName, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    best = editor;
                    break;
                }
            }

            if (best is not null)
                best.LinkedCommits.Add(commit);

            associations.Add(new CommitAssociation(commit, best));
        }

        return associations;
    }
}

public sealed record CommitAssociation(CommitEvent Commit, NormalizedEvent? EditorEvent);
