using System.Diagnostics;

public static class GitHelper
{
    public static async Task<IReadOnlyList<GitCommit>> GetCommitsByDate(string repoPath, CancellationToken ct, DateTime start, DateTime end)
    {
        var since = start.ToString("yyyy-MM-dd");
        var until = end.ToString("yyyy-MM-dd");

        var psi = new ProcessStartInfo("git",
            $"log --since=\"{since}\" --until=\"{until}\" --pretty=format:\"%H|%an|%ad|%s\" --date=iso")
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
            var parts = line.Split('|', 4);
            if (parts.Length == 4)
            {
                list.Add(new GitCommit(
                    parts[0],
                    parts[1],
                    DateTimeOffset.Parse(parts[2]),
                    parts[3]));
            }
        }
        return list;
    }
}

public sealed record GitCommit(string Hash, string Author, DateTimeOffset Date, string Message);
