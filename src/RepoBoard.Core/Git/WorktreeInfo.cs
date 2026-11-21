namespace RepoBoard.Core.Git;

/// <summary>One worktree from <c>git worktree list --porcelain -z</c> (WP-04).</summary>
/// <param name="Path">Full path, in the platform's form (git writes forward slashes on Windows).</param>
/// <param name="Head">The commit checked out; null in a repository without commits.</param>
/// <param name="Branch">The short branch name; null when detached (or bare).</param>
/// <param name="Locked">Locked against removal and pruning (<c>git worktree lock</c>): the reason, empty without one; null when not locked.</param>
/// <param name="Prunable">Its folder is gone: why git would prune it; null otherwise.</param>
public sealed record WorktreeInfo(string Path, string? Head, string? Branch, bool IsBare, string? Locked, string? Prunable)
{
    public bool IsLocked => Locked is not null;

    /// <summary>
    /// Parses <c>git worktree list --porcelain -z</c>: attributes end with NUL, an empty field ends a worktree. The first
    /// one is always the main worktree (or the bare repository).
    /// </summary>
    public static IReadOnlyList<WorktreeInfo> ParseList(string output)
    {
        var list = new List<WorktreeInfo>();
        WorktreeInfo? current = null;
        foreach (var field in output.Split('\0'))
        {
            if (field.Length == 0)
            {
                Flush();
                continue;
            }

            var space = field.IndexOf(' ', StringComparison.Ordinal);
            var (key, value) = space < 0 ? (field, "") : (field[..space], field[(space + 1)..]);
            if (key == "worktree")
            {
                Flush();
                // A broken entry (empty gitdir file after an aborted "worktree add") lists no path: left out – GetFullPath
                // would throw and stop the status of every repository (review WP-04).
                current = System.IO.Path.IsPathFullyQualified(value)
                    ? new WorktreeInfo(System.IO.Path.GetFullPath(value), null, null, false, null, null)
                    : null;
            }
            else if (current is not null)
            {
                current = key switch
                {
                    "HEAD" => current with { Head = value.Length > 0 && !value.All(c => c == '0') ? value : null },
                    "branch" => current with { Branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value },
                    "bare" => current with { IsBare = true },
                    "locked" => current with { Locked = value },
                    "prunable" => current with { Prunable = value },
                    _ => current,
                };
            }
        }

        Flush();
        return list;

        void Flush()
        {
            if (current is not null)
            {
                list.Add(current);
                current = null;
            }
        }
    }
}
