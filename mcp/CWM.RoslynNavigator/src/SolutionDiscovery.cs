namespace CWM.RoslynNavigator;

/// <summary>
/// Discovers .sln/.slnx files from command-line arguments or the working directory.
/// Uses breadth-first search up to <see cref="MaxSearchDepth"/> levels deep.
/// </summary>
internal static class SolutionDiscovery
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    /// <summary>
    /// Maximum directory depth to search for solution files (0 = root only).
    /// </summary>
    public const int MaxSearchDepth = 3;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj",
        "packages", "artifacts", "TestResults", ".claude"
    };

    /// <summary>
    /// Resolves the solution file path from the provided arguments or by scanning the working directory.
    /// </summary>
    /// <param name="args">Command-line arguments. Supports --solution / -s path.</param>
    /// <param name="workingDirectory">The directory to scan if no explicit path is provided.</param>
    /// <returns>The full path to the solution file, or null if not found.</returns>
    public static string? FindSolutionPath(string[] args, string? workingDirectory = null)
    {
        var explicitPath = GetExplicitSolutionPath(args);
        if (explicitPath is not null)
        {
            if (File.Exists(explicitPath))
                return Path.GetFullPath(explicitPath);

            if (Directory.Exists(explicitPath))
                return ScanDirectory(Path.GetFullPath(explicitPath));

            return null;
        }

        var directory = workingDirectory ?? Directory.GetCurrentDirectory();
        return ScanDirectory(directory);
    }

    private static string? GetExplicitSolutionPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--solution" or "-s")
            {
                return args[i + 1];
            }
        }
        return null;
    }

    /// <summary>
    /// Breadth-first search for solution files up to <see cref="MaxSearchDepth"/> levels deep.
    /// Shallowest match wins. Within the same depth, alphabetical (case-insensitive) ordering is used.
    /// </summary>
    private static string? ScanDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((directory, 0));

        while (queue.Count > 0)
        {
            var (currentDir, depth) = queue.Dequeue();

            var solutions = SafeGetSolutionFiles(currentDir);
            if (solutions.Count > 0)
                return solutions[0];

            if (depth >= MaxSearchDepth)
                continue;

            foreach (var subDir in SafeGetDirectories(currentDir))
            {
                var dirName = Path.GetFileName(subDir);
                if (!SkippedDirectories.Contains(dirName))
                    queue.Enqueue((subDir, depth + 1));
            }
        }

        return null;
    }

    private static List<string> SafeGetSolutionFiles(string directory)
    {
        try
        {
            return SolutionExtensions
                .SelectMany(ext => Directory.GetFiles(directory, $"*{ext}"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static string[] SafeGetDirectories(string directory)
    {
        try
        {
            var dirs = Directory.GetDirectories(directory);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            return dirs;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
