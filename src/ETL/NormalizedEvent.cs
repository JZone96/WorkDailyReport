namespace WorkDailyReport.ETL;

public sealed class NormalizedEvent
{
    public NormalizedEvent(
        DateTimeOffset tsStart,
        DateTimeOffset tsEnd,
        string source,
        string kind,
        string? app,
        string? title,
        string? url,
        bool isCoding)
    {
        TsStart = tsStart;
        TsEnd = tsEnd;
        Source = source;
        Kind = kind;
        App = app;
        Title = title;
        Url = url;
        IsCoding = isCoding;
    }

    public DateTimeOffset TsStart { get; }
    public DateTimeOffset TsEnd { get; }
    public TimeSpan Duration => TsEnd - TsStart;
    public string Source { get; }
    public string Kind { get; }
    public string? App { get; }
    public string? Title { get; }
    public string? Url { get; }
    public bool IsCoding { get; }
    public string? ProjectTag { get; private set; }
    public List<CommitEvent> LinkedCommits { get; } = new();

    public void AssignProject(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || ProjectTag is not null)
            return;

        ProjectTag = tag;
    }

    public NormalizedRow ToRow() =>
        new(
            TsStart,
            TsEnd,
            Duration.TotalSeconds < 0 ? 0 : Duration.TotalSeconds,
            App ?? string.Empty,
            Title ?? string.Empty,
            Url ?? string.Empty,
            Source,
            Kind,
            ProjectTag ?? string.Empty);
}

public sealed record NormalizedRow(
    DateTimeOffset TsStart,
    DateTimeOffset TsEnd,
    double DurationSeconds,
    string App,
    string Title,
    string Url,
    string Source,
    string Kind,
    string ProjectTag);
