using System.Collections.Concurrent;

namespace WorkDailyReport.utils;

public interface IGitRepoLocator
{
    Task<IReadOnlyList<string>> FindAsync(string root, CancellationToken ct);
}

public sealed class GitRepoLocator : IGitRepoLocator
{
    public async Task<IReadOnlyList<string>> FindAsync(string root, CancellationToken ct)
    {
        var bag = new ConcurrentBag<string>();
        if (Directory.Exists(Path.Combine(root, ".git")))
            bag.Add(root);

        var level1 = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly);

        await Parallel.ForEachAsync(level1, ct, async (dir, token) =>
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) { bag.Add(dir); return; }
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                if (Directory.Exists(Path.Combine(sub, ".git"))) bag.Add(sub);
            await Task.CompletedTask;
        });

        return bag.ToList();
    }
}
