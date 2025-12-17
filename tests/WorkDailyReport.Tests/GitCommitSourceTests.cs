using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using WorkDailyReport.ETL;
using WorkDailyReport.utils;

namespace WorkDailyReport.Tests;

public class GitCommitSourceTests
{
    [Fact]
    public async Task GetCommits_ForCurrentRepo_ReturnsAtLeastOneCommit()
    {
        var repoRoot = FindRepoRoot();
        var locator = new GitRepoLocator();
        var logger = NullLogger<GitCommitSource>.Instance;
        var source = new GitCommitSource(locator, logger);

        var start = DateTime.Today.AddYears(-10);
        var end = DateTime.Today.AddDays(1);
        var commits = await source.GetCommitsAsync(repoRoot, start, end, CancellationToken.None);

        Assert.NotEmpty(commits);
        Assert.Contains(commits, c => string.Equals(c.RepoPath, repoRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
                return current;

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Impossibile individuare la root del repository per i test.");
    }
}
