// src/Config/WorkReportOptions.cs
namespace WorkDailyReport.Config;

public sealed class WorkReportOptions
{
    public PathsOptions Paths { get; set; } = new();
    public WorkHoursOptions WorkHours { get; set; } = new();
    public ActivityWatchOptions ActivityWatch { get; set; } = new();
    public FiltersOptions Filters { get; set; } = new();
    public EditorsOptions Editors { get; set; } = new();
    public PrivacyOptions Privacy { get; set; } = new();
    public List<CategoryRule> Categories { get; set; } = new();
    public OutputOptions Output { get; set; } = new();
    public NotificationsOptions Notifications { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public ReposOptions Repos { get; set; } = new();
    public SummaryOptions Summary { get; set; } = new();
    public CalendarOptions Calendar { get; set; } = new();
    public ReportWindowOptions? ReportWindow { get; set; }
}

public class ReportWindowOptions
{
    public string? StartDate { get; set; } // "yyyy-MM-dd"
    public string? EndDate   { get; set; } // "yyyy-MM-dd"
}

public sealed class PathsOptions
{
    public string ReposRoot { get; set; } = "";
    public string ReportsDir { get; set; } = "reports";
    public string DataDir { get; set; } = "data";
}

public sealed class WorkHoursOptions
{
    public string Start { get; set; } = "09:00";
    public string End { get; set; } = "18:00";
    public LunchBreakOptions LunchBreak { get; set; } = new();
    public Dictionary<string, DailyWorkHoursOverride> DailyOverrides { get; set; } = new();
    public List<DayOfWeek> WorkDays { get; set; } =
        new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
    public string TimeZone { get; set; } = "Europe/Rome";
}

public sealed class LunchBreakOptions
{
    public string Start { get; set; } = "13:30";
    public string End { get; set; } = "14:30";
}

public sealed class DailyWorkHoursOverride
{
    public string? Start { get; set; }
    public string? End { get; set; }
    public LunchBreakOptions? LunchBreak { get; set; }
}

public sealed class ActivityWatchOptions
{
    // Nota: nel tuo JSON include già /api/0
    public string BaseUrl { get; set; } = "http://127.0.0.1:5600/api/0";
    public List<string> Watchers { get; set; } =
        new() { "aw-watcher-window", "aw-watcher-web", "aw-watcher-afk" };
}

public sealed class FiltersOptions
{
    public int MinDurationSeconds { get; set; } = 10;
    public int MergeGapSeconds { get; set; } = 60;
    public bool ExcludeAFK { get; set; } = true;
}

public sealed class EditorsOptions
{
    public List<string> RecognizedApps { get; set; } = new();
    public bool NormalizeTitle { get; set; } = true;
    public int CommitAssociationWindowMinutes { get; set; } = 15;
}

public sealed class PrivacyOptions
{
    public List<string> BlacklistApps { get; set; } = new();
    public List<string> BlacklistDomains { get; set; } = new();
    public bool AnonymizeUrls { get; set; } = true;
}

public sealed class CategoryRule
{
    public string Name { get; set; } = "";
    public List<string> Patterns { get; set; } = new();
}

public sealed class OutputOptions
{
    public string ReportsFolder { get; set; } = "reports";
    public List<string> Format { get; set; } = new() { "Markdown", "Csv" };
    public string FileNameTemplate { get; set; } = "daily-{date:yyyy-MM-dd}.md";
}

public sealed class NotificationsOptions
{
    public EmailOptions Email { get; set; } = new();
    public SlackOptions Slack { get; set; } = new();
    public NotionOptions Notion { get; set; } = new();
}

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int Port { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class SlackOptions
{
    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = "";
}

public sealed class NotionOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string DatabaseId { get; set; } = "";
}

public sealed class DatabaseOptions
{
    public string Type { get; set; } = "SQLite";
    public string ConnectionString { get; set; } = "Data Source=data\\workreport.db;";
}

public sealed class ReposOptions
{
    public string ReposRootFolder { get; set; } = "";
}

public sealed class SummaryOptions
{
    public bool UseLLM { get; set; }
    public string LLMProvider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
}

public sealed class CalendarOptions
{
    public OutlookCalendarOptions Outlook { get; set; } = new();
}

public sealed class OutlookCalendarOptions
{
    public bool Enabled { get; set; }
    public string IcsFile { get; set; } = "";
    public string? TimeZone { get; set; }
}
