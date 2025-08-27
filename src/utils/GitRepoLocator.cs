using System.Diagnostics;

namespace WorkDailyReport.Utils
{
    public static class GitRepoLocator
    {
        /// <summary>
        /// Scansiona ricorsivamente una cartella di root alla ricerca di repository Git.
        /// Una repo è riconosciuta se contiene la cartella ".git".
        /// </summary>
        public static IEnumerable<string> FindRepositoriesInFolder(string rootPath)
        {
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Cartella non trovata: {rootPath}");

            var repos = new List<string>();

            // Cerca tutti i ".git" ricorsivamente
            foreach (var dir in Directory.EnumerateDirectories(rootPath, ".git", SearchOption.AllDirectories))
            {
                var repoRoot = Directory.GetParent(dir)?.FullName;
                if (repoRoot is not null)
                    repos.Add(repoRoot);
            }

            return repos.Distinct();
        }

        /// <summary>
        /// Usa "git rev-parse --show-toplevel" per ottenere la root della repo
        /// partendo da una qualsiasi sottocartella interna.
        /// </summary>
        public static string? GetCurrentRepositoryRoot(string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --show-toplevel",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            string output = proc.StandardOutput.ReadToEnd().Trim();
            string err = proc.StandardError.ReadToEnd().Trim();

            proc.WaitForExit();

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output;

            Console.WriteLine($"Errore git rev-parse: {err}");
            return null;
        }
    }
}
