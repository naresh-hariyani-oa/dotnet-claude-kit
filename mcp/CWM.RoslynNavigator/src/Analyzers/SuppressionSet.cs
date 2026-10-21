using System.Text.Json;
using System.Text.Json.Serialization;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Repo-level suppression rules loaded from <c>.cwm-navigator.json</c>.
/// Lets a codebase declare that a detector is wrong for it — for example, that
/// broad catch blocks under an outbox drainer are sanctioned resilience wrappers.
/// Suppressed findings are still counted in the summary, never silently dropped.
/// </summary>
internal sealed class SuppressionSet
{
    public const string ConfigFileName = ".cwm-navigator.json";

    private readonly HashSet<string> _disabled;
    private readonly List<PathRule> _pathRules;

    private SuppressionSet(HashSet<string> disabled, List<PathRule> pathRules)
    {
        _disabled = disabled;
        _pathRules = pathRules;
    }

    public static SuppressionSet Empty { get; } = new([], []);

    /// <summary>Whether any rule was loaded — surfaced in the tool response for transparency.</summary>
    public bool HasRules => _disabled.Count > 0 || _pathRules.Count > 0;

    public string? ConfigPath { get; private init; }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for <c>.cwm-navigator.json</c>,
    /// stopping at the repository root. Returns <see cref="Empty"/> when no config exists or
    /// the config is malformed — a broken config must never break analysis.
    /// </summary>
    public static SuppressionSet Load(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return Empty;

        var directory = Directory.Exists(startDirectory)
            ? new DirectoryInfo(startDirectory)
            : new FileInfo(startDirectory).Directory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ConfigFileName);
            if (File.Exists(candidate))
                return LoadFrom(candidate);

            // Stop at the repository root.
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                break;

            directory = directory.Parent;
        }

        return Empty;
    }

    private static SuppressionSet LoadFrom(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<NavigatorConfig>(json, ConfigOptions);
            var section = config?.AntiPatterns;

            if (section is null)
                return Empty;

            var disabled = new HashSet<string>(
                section.Disable ?? [],
                StringComparer.OrdinalIgnoreCase);

            var rules = new List<PathRule>();
            foreach (var entry in section.Suppress ?? [])
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Paths is not { Length: > 0 })
                    continue;

                rules.Add(new PathRule(entry.Id, entry.Paths, entry.Reason ?? "suppressed by .cwm-navigator.json"));
            }

            return new SuppressionSet(disabled, rules) { ConfigPath = configPath };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed or unreadable config degrades to no suppression rather than failing.
            return Empty;
        }
    }

    /// <summary>Whether a detector is switched off entirely for this repository.</summary>
    public bool IsDisabled(string id) => _disabled.Contains(id);

    /// <summary>
    /// The reason a finding is suppressed at this path, or null if it is not suppressed.
    /// A rule id of <c>*</c> suppresses every detector under the matching paths.
    /// </summary>
    public string? PathSuppressionReason(string id, string relativePath)
    {
        foreach (var rule in _pathRules)
        {
            if (!rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && rule.Id != "*")
                continue;

            foreach (var pattern in rule.Paths)
            {
                if (GlobMatcher.IsMatch(pattern, relativePath))
                    return rule.Reason;
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record PathRule(string Id, string[] Paths, string Reason);

    private sealed class NavigatorConfig
    {
        [JsonPropertyName("antipatterns")]
        public AntiPatternSection? AntiPatterns { get; set; }
    }

    private sealed class AntiPatternSection
    {
        [JsonPropertyName("disable")]
        public string[]? Disable { get; set; }

        [JsonPropertyName("suppress")]
        public SuppressEntry[]? Suppress { get; set; }
    }

    private sealed class SuppressEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("paths")]
        public string[]? Paths { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}

/// <summary>
/// Minimal glob matcher for suppression paths. Supports <c>**</c> (any number of segments),
/// <c>*</c> (any characters within one segment), and literal segments. Matching is
/// case-insensitive and separator-agnostic.
/// </summary>
internal static class GlobMatcher
{
    public static bool IsMatch(string pattern, string path)
    {
        var patternSegments = Split(pattern);
        var pathSegments = Split(path);

        return Match(patternSegments, 0, pathSegments, 0);
    }

    private static string[] Split(string value) =>
        value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool Match(string[] pattern, int pi, string[] path, int si)
    {
        while (pi < pattern.Length)
        {
            if (pattern[pi] == "**")
            {
                // Trailing ** matches everything that remains.
                if (pi == pattern.Length - 1)
                    return true;

                for (var skip = si; skip <= path.Length; skip++)
                {
                    if (Match(pattern, pi + 1, path, skip))
                        return true;
                }

                return false;
            }

            if (si >= path.Length)
                return false;

            if (!SegmentMatches(pattern[pi], path[si]))
                return false;

            pi++;
            si++;
        }

        return si == path.Length;
    }

    private static bool SegmentMatches(string pattern, string segment)
    {
        if (pattern == "*")
            return true;

        if (!pattern.Contains('*'))
            return string.Equals(pattern, segment, StringComparison.OrdinalIgnoreCase);

        // Wildcard within a segment: match the literal parts in order.
        var parts = pattern.Split('*');
        var index = 0;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;

            var found = segment.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return false;

            // A leading literal must anchor to the start.
            if (i == 0 && found != 0)
                return false;

            index = found + part.Length;
        }

        // A trailing literal must anchor to the end.
        var last = parts[^1];
        if (last.Length > 0 && !segment.EndsWith(last, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
