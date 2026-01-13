using System.Diagnostics;
using System.Globalization;

public static class GitHelper
{
    public static async Task<IReadOnlyList<GitCommit>> GetCommitsByDate(
        string repoPath,
        CancellationToken ct,
        DateTimeOffset since,
        DateTimeOffset until)
    {
        var sinceArg = since.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var untilArg = until.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo("git",
            $"log --all --since=\"{sinceArg}\" --until=\"{untilArg}\" --pretty=format:\"%H|%an|%ae|%ad|%s\" --date=iso-strict")
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync(ct);

        var list = new List<GitCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|', 5);
            if (parts.Length == 5)
            {
                var author = string.IsNullOrWhiteSpace(parts[2])
                    ? parts[1]
                    : $"{parts[1]} <{parts[2]}>";
                list.Add(new GitCommit(
                    parts[0],
                    author,
                    DateTimeOffset.Parse(parts[3]),
                    parts[4]));
            }
        }
        return list;
    }
}

public sealed record GitCommit(string Hash, string Author, DateTimeOffset Date, string Message);
