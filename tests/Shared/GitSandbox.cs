using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RepoBoard.Testing;

/// <summary>
/// Real git repositories in a <see cref="TestFolder"/>: a bare "remote", the clone on the board (<see cref="Mine"/>) and a
/// second clone that pushes like a colleague would (<see cref="Theirs"/>). Every git call of the test process – the
/// test's own and RepoBoard's through <c>GitRunner</c>, which inherits the environment – runs without the machine's git
/// config (no signing, hooks, aliases or default branch of the developer) and with a fixed identity, so the tests run the
/// same on every machine and in the CI.
/// </summary>
internal sealed class GitSandbox : IDisposable
{
    private readonly TestFolder _folder = new();

    [ModuleInitializer]
    internal static void IsolateGitConfig()
    {
        var empty = Path.Combine(Path.GetTempPath(), "repoboard-test-gitconfig");
        File.WriteAllText(empty, "");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", empty);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Test");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "test@example.invalid");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Test");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "test@example.invalid");

        // A repository above the temp folder (dotfiles in $HOME) must not answer for a sandbox whose .git is gone (review WP-03).
        Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", Path.TrimEndingDirectorySeparator(Path.GetTempPath()));

        // Run from a hook or a terminal with a repository of its own, the sandbox's git calls would work on that one
        // (GIT_DIR, GIT_INDEX_FILE …) – the same variables RepoBoard's runner leaves out (review 0.4.0, A20).
        foreach (var name in RepoBoard.Core.Git.GitRunner.Inherited.Append("GIT_ASKPASS"))
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public GitSandbox()
    {
        RequireGit();
        Remote = _folder.Combine("remote.git");
        Mine = _folder.Combine("mine");
        Theirs = _folder.Combine("theirs");
        Git(_folder.Path, "init", "--bare", "-b", "main", Remote);
        Git(_folder.Path, "clone", Remote, Theirs);
        Commit(Theirs, "a.txt", "one");
        Git(Theirs, "push", "-u", "origin", "main");
        Git(_folder.Path, "clone", Remote, Mine);
    }

    public string Root => _folder.Path;
    public string Remote { get; }
    public string Mine { get; }
    public string Theirs { get; }

    /// <summary>Writes <paramref name="file"/> and commits it.</summary>
    public static void Commit(string repo, string file, string content)
    {
        File.WriteAllText(Path.Combine(repo, file), content);
        Git(repo, "add", "-A");
        Git(repo, "commit", "-m", $"{file}: {content}");
    }

    /// <summary>git without failing the test: false when it ends with an error (a feature the platform lacks).</summary>
    public static bool TryGit(string dir, params string[] args) => Run(dir, args).ExitCode == 0;

    public static string Git(string dir, params string[] args)
    {
        var (exitCode, output, error) = Run(dir, args);
        Assert.True(exitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output;
    }

    private static (int ExitCode, string Output, string Error) Run(string dir, string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }

    /// <summary>Skips without git; in the CI (<c>CI=true</c>) a missing git is a failure, not a skip.</summary>
    private static void RequireGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true })!;
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        Assert.SkipWhen(Environment.GetEnvironmentVariable("CI") != "true", "git is not installed.");
        Assert.Fail("git is not installed on the CI runner.");
    }

    /// <summary>git marks its object files read-only; <see cref="TestFolder"/> deletes through <c>SafeDelete</c>.</summary>
    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _folder.Dispose();
    }
}
