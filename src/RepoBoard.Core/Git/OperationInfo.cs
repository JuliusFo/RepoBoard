namespace RepoBoard.Core.Git;

/// <summary>
/// A merge, rebase, cherry-pick or revert left unfinished (WP-07), as git's own files in the git folder describe it – read,
/// never written. What the row shows and what an abort or skip must still find when it runs: <see cref="Key"/> changes when
/// the operation goes on or another one starts, so a click on what was shown never acts on something newer (review WP-04).
/// </summary>
/// <param name="Kind">Which operation.</param>
/// <param name="Key">Names this operation at this step: the commit it brings in (rebase: the branch's commit before it and the
/// step) and HEAD.</param>
/// <param name="Subject">First line of the message git prepared – "Merge branch 'x'", the picked or reverted commit's subject,
/// the subject of the commit a rebase stopped at; null if git wrote none.</param>
/// <param name="Commit">The commit it brings in or stopped at (full id); null if git does not say.</param>
/// <param name="Branch">Rebase only: the branch being rebased (<c>feature</c>); null for a detached HEAD.</param>
/// <param name="Step">Rebase only: the commit it is at, counted from 1.</param>
/// <param name="Steps">Rebase only: how many commits it replays.</param>
public sealed record OperationInfo(GitOperation Kind, string Key, string? Subject, string? Commit, string? Branch, int? Step, int? Steps)
{
    /// <summary>The unfinished operation in <paramref name="gitDir"/>, or null (also if its files cannot be read right now).</summary>
    public static OperationInfo? Read(string gitDir)
    {
        if (GitClient.OperationIn(gitDir) is not { } kind)
        {
            return null;
        }

        try
        {
            return kind switch
            {
                GitOperation.Merge => Picked(kind, gitDir, "MERGE_HEAD"),
                GitOperation.CherryPick => Picked(kind, gitDir, "CHERRY_PICK_HEAD"),
                GitOperation.Revert => Picked(kind, gitDir, "REVERT_HEAD"),
                _ => Rebase(gitDir),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // git is just writing or removing them: still unfinished (auto-pull keeps out), but with a key no click matches –
            // not even one from another read that failed the same way (review WP-07).
            return new OperationInfo(kind, $"{kind}:?{Guid.NewGuid():N}", null, null, null, null, null);
        }
    }

    /// <summary>
    /// What HEAD is (the file, so a commit id while a rebase runs detached): part of every key. A commit made meanwhile in
    /// the terminal – at an "edit" stop, or "git commit" instead of continue – changes it, and an abort would drop that
    /// commit (review WP-07).
    /// </summary>
    private static string? Head(string gitDir) => FirstLine(Text(gitDir, "HEAD"));

    /// <summary>Merge, cherry-pick, revert: the commit in the marker file, the message git prepared in MERGE_MSG.</summary>
    private static OperationInfo Picked(GitOperation kind, string gitDir, string headFile)
    {
        // MERGE_HEAD of an octopus merge holds one commit per line.
        var commit = FirstLine(Text(gitDir, headFile));
        return new OperationInfo(kind, $"{kind}:{commit}:{Head(gitDir)}", FirstLine(Text(gitDir, "MERGE_MSG")), commit, null, null, null);
    }

    /// <summary>
    /// A rebase: <c>rebase-merge</c> (the default backend, also <c>-i</c>) or <c>rebase-apply</c> (<c>--apply</c>, and
    /// <c>git am</c>, which git reports the same way).
    /// </summary>
    private static OperationInfo Rebase(string gitDir)
    {
        var merge = Path.Combine(gitDir, "rebase-merge");
        var folder = Directory.Exists(merge) ? merge : Path.Combine(gitDir, "rebase-apply");
        var isMerge = folder == merge;
        var step = Number(Text(folder, isMerge ? "msgnum" : "next"));
        var steps = Number(Text(folder, isMerge ? "end" : "last"));
        var original = FirstLine(Text(folder, "orig-head"));
        var headName = FirstLine(Text(folder, "head-name"));
        var branch = headName?.StartsWith("refs/heads/", StringComparison.Ordinal) == true ? headName["refs/heads/".Length..] : null;
        // Where it stopped: REBASE_HEAD (git 2.17) – also when it stopped at an "edit" without a conflict.
        var commit = FirstLine(Text(gitDir, "REBASE_HEAD")) ?? (isMerge ? FirstLine(Text(folder, "stopped-sha")) : null);
        var subject = FirstLine(Text(folder, isMerge ? "message" : "final-commit")) ?? FirstLine(Text(gitDir, "MERGE_MSG"));
        return new OperationInfo(GitOperation.Rebase, $"Rebase:{original}:{step}:{commit}:{Head(gitDir)}", subject, commit, branch, step, steps);
    }

    private static string? Text(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>The first line that is not empty and not a comment of git's (<c>#</c> in MERGE_MSG).</summary>
    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));

    private static int? Number(string? text) => int.TryParse(text?.Trim(), out var n) ? n : null;
}
