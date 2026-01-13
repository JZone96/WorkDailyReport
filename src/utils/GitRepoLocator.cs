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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(Path.Combine(root, ".git")))
            bag.Add(root);

        await Task.Run(() =>
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();

                IEnumerable<string> subdirs;
                try
                {
                    subdirs = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var dir in subdirs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seen.Add(dir))
                        continue;

                    if (string.Equals(Path.GetFileName(dir), ".git", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (Directory.Exists(Path.Combine(dir, ".git")))
                    {
                        bag.Add(dir);
                        continue;
                    }

                    stack.Push(dir);
                }
            }
        }, ct);

        return bag.ToList();
    }
}
