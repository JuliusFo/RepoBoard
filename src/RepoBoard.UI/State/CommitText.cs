namespace RepoBoard.UI.State;

/// <summary>
/// How the page writes commits, refs, paths and branches – once, not in each component (review 0.4.0, B7: the short id at
/// four places, the refs and the rename tooltip twice).
/// </summary>
public static class CommitText
{
    /// <summary>A commit id cut to seven characters (where git did not choose an abbreviation).</summary>
    public static string? Short(string? id) => id?[..Math.Min(7, id.Length)];

    /// <summary>git's <c>%D</c> as names: "HEAD -> main, origin/main, tag: v1.0" → main, origin/main, tag: v1.0.</summary>
    public static IEnumerable<string> RefNames(string refs) =>
        refs.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(r => r.Replace("HEAD -> ", "", StringComparison.Ordinal));

    /// <summary>A file's path for a tooltip; a renamed one with where it came from.</summary>
    public static string PathTitle(string path, string? from) => from is null ? path : $"{from} → {path}";

    /// <summary>A branch the badge shows as the main line rather than a feature.</summary>
    public static bool IsMainBranch(string? name) => name is "main" or "master" or "develop" or "dev";
}
