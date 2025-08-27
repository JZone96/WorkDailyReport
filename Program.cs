using WorkDailyReport.ActivityWatch;
using WorkDailyReport.Utils; // per DateTimeHelpers

var http = new HttpClient();
var awClient = new ActivityWatchClient(http, "http://127.0.0.1:5600/api/0");

// Recupero i buckets disponibili
var buckets = await awClient.ListBucketsAsync();
if (buckets.Count == 0)
{
    Console.WriteLine("⚠ Nessun bucket trovato, avvia ActivityWatch.");
    return;
}
Console.WriteLine("Buckets disponibili:");
foreach (var b in buckets)
{
    Console.WriteLine($"- {b.Id} (Client={b.Client})");
}

// Scelgo il primo bucket di tipo "window"
var windowBucket = buckets.FirstOrDefault(b => b.Id.Contains("aw-watcher-window"));
if (windowBucket == null)
{
    Console.WriteLine("⚠ Nessun bucket window trovato.");
    return;
}

// Definisco un intervallo (oggi 09:00–18:00)
var today = DateTimeOffset.Now.Date;
var start = today.AddHours(9);
var end = today.AddHours(18);

Console.WriteLine($"DA {start} A {end}.");
// Chiamo GetEventsAsync
var events = await awClient.GetEventsAsync(windowBucket.Id, start, end);

Console.WriteLine($"Ho trovato {events.Count} eventi nel bucket {windowBucket.Id}.");
foreach (var ev in events.Take(10)) // stampo solo i primi 10
{
    Console.WriteLine($"- {ev.Timestamp} ({ev.Duration}s) → {ev.Data.App} | {ev.Data.Title} | {ev.Data.Url}");
}
// cerco i repositori nel git locale (tutte le cartelle contentnti la cartella .git)

string basePath = @"D:\gitProjects";

foreach (var dir in Directory.EnumerateDirectories(basePath, "*", SearchOption.TopDirectoryOnly))
{
    var gitDir = Path.Combine(dir, ".git");
    if (Directory.Exists(gitDir))
    {
        Console.WriteLine($"Repo trovata: {dir}");
        // Qui faccio continue → non scendo dentro alla repo
        continue;
    }

    // Se vuoi scendere ancora (ma solo se non è repo)
    foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
    {
        var subGit = Path.Combine(sub, ".git");
        if (Directory.Exists(subGit))
        {
            Console.WriteLine($"Repo trovata: {sub}");
            continue; // salto esplorazione interna
        }
    }
}

