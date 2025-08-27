namespace WorkDailyReport.Config
{
    // Radice della sezione "WorkReport" in appsettings.json
    public class WorkReportConfig
    {
        public WorkHoursConfig WorkHours { get; set; } = new();
        public ActivityWatchConfig ActivityWatch { get; set; } = new();
        public FiltersConfig Filters { get; set; } = new();
        public EditorsConfig Editors { get; set; } = new();
        public PrivacyConfig Privacy { get; set; } = new();
        public List<CategoryConfig> Categories { get; set; } = new();
        public OutputConfig Output { get; set; } = new();
        public DatabaseConfig Database { get; set; } = new();
    }

    public class WorkHoursConfig
    {
        public string Start { get; set; }
        public string End { get; set; } = "18:00";
        public LunchBreakConfig LunchBreak { get; set; } = new();
        public List<string> WorkDays { get; set; } = new();
    }

    public class LunchBreakConfig
    {
        public string Start { get; set; } = "13:00";
        public string End { get; set; } = "14:00";
    }

    public class ActivityWatchConfig
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:5600/api/0";
        public List<string> Watchers { get; set; } = new();
    }

    public class FiltersConfig
    {
        public int MinDurationSeconds { get; set; } = 10;
        public int MergeGapSeconds { get; set; } = 60;
        public bool ExcludeAFK { get; set; } = true;
    }

    public class EditorsConfig
    {
        public List<string> RecognizedApps { get; set; } = new();
        public bool NormalizeTitle { get; set; } = true;
        public int CommitAssociationWindowMinutes { get; set; } = 15;
    }

    public class PrivacyConfig
    {
        public List<string> BlacklistApps { get; set; } = new();
        public List<string> BlacklistDomains { get; set; } = new();
        public bool AnonymizeUrls { get; set; } = true;
    }

    public class CategoryConfig
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Patterns { get; set; } = new();
    }

    public class OutputConfig
    {
        public string ReportsFolder { get; set; } = "reports";
        public List<string> Format { get; set; } = new(); // es. Markdown, Csv
        public string FileNameTemplate { get; set; } = "daily-{date:yyyy-MM-dd}.md";
    }

    public class DatabaseConfig
    {
        public string Type { get; set; } = "SQLite";
        public string ConnectionString { get; set; } = "Data Source=data\\workreport.db;";
    }
    public class ReposConfig
    {
        public string ReposRootFolder { get; set; } = "D:\\gitProjects";
    }
}
