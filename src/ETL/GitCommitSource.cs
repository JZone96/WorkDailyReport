using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using WorkDailyReport.utils;
using System.Linq;

namespace WorkDailyReport.ETL;

public interface IGitCommitSource
{
    Task<IReadOnlyList<CommitEvent>> GetCommitsAsync(
        string reposRoot,
        DateTime startDate,
        DateTime endDateExclusive,
        CancellationToken ct);
}

public sealed class GitCommitSource : IGitCommitSource
{
    private readonly IGitRepoLocator _repoLocator;
    private readonly ILogger<GitCommitSource> _logger;

    public GitCommitSource(IGitRepoLocator repoLocator, ILogger<GitCommitSource> logger)
    {
        _repoLocator = repoLocator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommitEvent>> GetCommitsAsync(
        string reposRoot,
        DateTime startDate,
        DateTime endDateExclusive,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reposRoot) || !Directory.Exists(reposRoot))
        {
            _logger.LogWarning("Repos root '{Root}' non valido per estrazione commit.", reposRoot);
            return Array.Empty<CommitEvent>();
        }

        var repoPaths = await _repoLocator.FindAsync(reposRoot, ct);
        if (repoPaths.Count == 0)
            return Array.Empty<CommitEvent>();

        var bag = new ConcurrentBag<CommitEvent>();
        await Parallel.ForEachAsync(repoPaths, ct, async (repo, token) =>
        {
            try
            {
                var commits = await GitHelper.GetCommitsByDate(repo, token, startDate, endDateExclusive);
                var repoName = Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                foreach (var commit in commits)
                {
                    bag.Add(new CommitEvent(
                        RepoName: repoName,
                        RepoPath: repo,
                        Hash: commit.Hash,
                        Author: commit.Author,
                        Timestamp: commit.Date,
                        Message: commit.Message));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante l'estrazione commit per {Repo}", repo);
            }
        });

        return bag
            .OrderBy(c => c.Timestamp)
            .ToList();
    }
}

public sealed record CommitEvent(
    string RepoName,
    string RepoPath,
    string Hash,
    string Author,
    DateTimeOffset Timestamp,
    string Message);
