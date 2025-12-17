namespace WorkDailyReport.ETL;

public sealed class NormalizedEvent
{
    public NormalizedEvent(
        DateTimeOffset start,
        DateTimeOffset end,
        string source,
        string kind,
        string? app,
        string? title,
        string? url,
        bool isCoding)
    {
        Start = start;
        End = end;
        Source = source;
        Kind = kind;
        App = app;
        Title = title;
        Url = url;
        IsCoding = isCoding;
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration => End - Start;
    public string Source { get; }
    public string Kind { get; }
    public string? App { get; }
    public string? Title { get; }
    public string? Url { get; }
    public bool IsCoding { get; }
    public List<CommitEvent> LinkedCommits { get; } = new();
}
