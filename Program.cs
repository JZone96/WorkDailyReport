using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkDailyReport.ActivityWatch;
using WorkDailyReport.Config;
using WorkDailyReport.ETL;
using WorkDailyReport.utils;
using WorkDailyReport.Calendar;

var builder = Host.CreateApplicationBuilder(args);
// carica config da ./config/appsettings.json (+ opzionale env)
builder.Configuration.AddJsonFile(System.IO.Path.Combine("config", "appsettings.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(System.IO.Path.Combine("config", $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true);

// logging minimale
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// diag veloce
Console.WriteLine("ContentRoot: " + builder.Environment.ContentRootPath);
Console.WriteLine("ReposRoot cfg: " + builder.Configuration["WorkReport:Paths:ReposRoot"]);


// bind TUTTO WorkReport
builder.Services.AddOptions<WorkReportOptions>()
    .Bind(builder.Configuration.GetSection("WorkReport"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Paths.ReposRoot), "WorkReport.Paths.ReposRoot vuoto")
    .Validate(o => Directory.Exists(o.Paths.ReposRoot), "WorkReport.Paths.ReposRoot non valido")
    .Validate(o => Uri.IsWellFormedUriString(o.ActivityWatch.BaseUrl, UriKind.Absolute),
              "WorkReport.ActivityWatch.BaseUrl non valido")
    .ValidateOnStart();

// HttpClient → ActivityWatch
builder.Services.AddHttpClient<IActivityWatchClient, ActivityWatchClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<WorkReportOptions>>().Value;
    http.BaseAddress = new Uri(opt.ActivityWatch.BaseUrl.TrimEnd('/') + "/");
});

// servizi app
builder.Services.AddSingleton<IGitRepoLocator, GitRepoLocator>();
builder.Services.AddSingleton<IGitCommitSource, GitCommitSource>();
builder.Services.AddSingleton<ICalendarEventSource, OutlookCalendarSource>();
builder.Services.AddSingleton<DailyRunner>();

await builder.Build()
    .Services.GetRequiredService<DailyRunner>()
    .RunAsync(CancellationToken.None);
