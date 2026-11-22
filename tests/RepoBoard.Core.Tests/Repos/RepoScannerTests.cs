using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Repos;

/// <summary>
/// What a scan finds and what each repository would run, read without git (review 0.4.0, A9): a repository's own
/// configuration runs with every git call RepoBoard makes there.
/// </summary>
public sealed class RepoScannerTests : IDisposable
{
    private readonly TestFolder _folder = new();

    private string Repo(string name, string? config = null, params string[] hooks)
    {
        var path = _folder.Combine("scan", name);
        var git = Path.Combine(path, ".git");
        Directory.CreateDirectory(Path.Combine(git, "hooks"));
        File.WriteAllText(Path.Combine(git, "config"), config ?? "[core]\n\tbare = false\n");
        File.WriteAllText(Path.Combine(git, "hooks", "pre-commit.sample"), "#!/bin/sh");
        foreach (var hook in hooks)
        {
            File.WriteAllText(Path.Combine(git, "hooks", hook), "#!/bin/sh");
        }

        return path;
    }

    private ScanCandidate Inspect(string repo) => RepoScanner.Inspect(repo, _folder.Combine("scan"));

    [Fact]
    public void A_plain_repository_runs_nothing_of_its_own()
    {
        var candidate = Inspect(Repo("plain"));

        Assert.True(candidate.Plain);
        Assert.Empty(candidate.Runs);
    }

    [Theory]
    [InlineData("[core]\n\tfsmonitor = calc.exe\n", RepoRuns.FsMonitor)]
    [InlineData("[Core]\n  FSMonitor=\"C:/tools/watch.cmd\"\n", RepoRuns.FsMonitor)]
    [InlineData("[core]\n\thooksPath = ../hooks\n", RepoRuns.HooksPath)]
    [InlineData("[core]\n\tsshCommand = ssh -i key\n", RepoRuns.SshCommand)]
    [InlineData("[include]\n\tpath = ../more.config\n", RepoRuns.Includes)]
    [InlineData("[includeIf \"gitdir:~/\"]\n\tpath = x\n", RepoRuns.Includes)]
    // Second review pass R1: a positive list – a filter runs on git status, a diff driver on a click, a helper on fetch.
    [InlineData("[filter \"x\"]\n\tclean = calc\n", RepoRuns.OtherSettings)]
    [InlineData("[diff \"x\"]\n\ttextconv = calc\n", RepoRuns.OtherSettings)]
    [InlineData("[credential]\n\thelper = !calc\n", RepoRuns.OtherSettings)]
    [InlineData("[remote \"origin\"]\n\turl = ext::calc %G\n", RepoRuns.OtherSettings)]
    [InlineData("[remote \"origin\"]\n\tuploadpack = calc\n", RepoRuns.OtherSettings)]
    [InlineData("[core]\n\taskPass = calc\n", RepoRuns.OtherSettings)]
    [InlineData("[submodule \"s\"]\n\tupdate = !calc\n", RepoRuns.OtherSettings)]
    public void What_the_configuration_makes_git_run_is_named(string config, RepoRuns runs)
    {
        var candidate = Inspect(Repo("r", config));

        Assert.Equal([runs], candidate.Runs);
        Assert.False(candidate.Plain);
    }

    [Theory]
    [InlineData("[core]\n\tfsmonitor = false\n")]
    [InlineData("[core]\n\tfsmonitor = true\n")] // git's own daemon, nothing of the repository's
    [InlineData("[core]\n\t# fsmonitor = calc\n")]
    // What a clone, a branch, LFS and a configured merge tool write: no hint, ticked at first.
    [InlineData("[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = false\n\tlogallrefupdates = true\n\tsymlinks = false\n\tignorecase = true\n"
        + "[remote \"origin\"]\n\turl = https://example.invalid/r.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n"
        + "[branch \"main\"]\n\tremote = origin\n\tmerge = refs/heads/main\n[lfs]\n\trepositoryformatversion = 0\n"
        + "[extensions]\n\tworktreeConfig = true\n[submodule \"s\"]\n\tactive = true\n[mergetool]\n\tcmd = tool\n[diff]\n\ttool = x\n")]
    public void Settings_that_run_nothing_of_the_repository_s_are_not_named(string config) =>
        Assert.Empty(Inspect(Repo("r", config)).Runs);

    /// <summary>With <c>extensions.worktreeConfig</c> git reads <c>config.worktree</c> on top – so does the scan (second review pass R1).</summary>
    [Fact]
    public void Config_worktree_is_read_too()
    {
        var repo = Repo("r", "[extensions]\n\tworktreeConfig = true\n");
        File.WriteAllText(Path.Combine(repo, ".git", "config.worktree"), "[core]\n\tfsmonitor = calc\n");

        Assert.Equal([RepoRuns.FsMonitor], Inspect(repo).Runs);
    }

    /// <summary>
    /// A .git file naming a git folder on another computer's share: not read before the user ticks it, never ticked at first
    /// (second review pass R1).
    /// </summary>
    [Fact]
    public void A_git_folder_on_a_share_is_named_and_not_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Shares are written as \\\\host\\share on Windows.");
        var repo = _folder.Combine("scan", "elsewhere");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: \\\\host.invalid\\share\\x\n");

        var candidate = Inspect(repo);

        Assert.Equal([RepoRuns.GitDirOnShare], candidate.Runs);
        Assert.False(candidate.Plain);
    }

    [Fact]
    public void Hooks_of_its_own_are_named_git_s_samples_are_not()
    {
        Assert.Equal([RepoRuns.Hooks], Inspect(Repo("hooked", null, "post-merge")).Runs);
    }

    /// <summary>A linked worktree in the folder whose repository lies outside: that repository goes on the board, and the list says so.</summary>
    [Fact]
    public void A_linked_worktree_names_its_repository_and_whether_it_lies_outside_the_folder()
    {
        var main = _folder.Combine("elsewhere", "main");
        var mainGit = Path.Combine(main, ".git");
        var worktreeGit = Path.Combine(mainGit, "worktrees", "wt");
        Directory.CreateDirectory(worktreeGit);
        File.WriteAllText(Path.Combine(mainGit, "config"), "[core]\n\tfsmonitor = calc\n");
        File.WriteAllText(Path.Combine(worktreeGit, "commondir"), "../..");
        var worktree = _folder.Combine("scan", "wt");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {worktreeGit}\n");

        var candidate = Assert.Single(RepoScanner.Candidates(_folder.Combine("scan")));

        Assert.Equal(worktree, candidate.Path);
        Assert.Equal(main, candidate.Board);
        Assert.True(candidate.OutsideScan);
        Assert.Equal([RepoRuns.FsMonitor], candidate.Runs); // the repository's settings apply in its worktrees
    }

    /// <summary>
    /// What a scan does not descend into: build and package folders, dot folders, links to elsewhere (a junction to a share or
    /// to C:\Users) – none of these lines ran in a test before (review 0.4.0, C8).
    /// </summary>
    [Fact]
    public void A_scan_skips_build_folders_dot_folders_and_links()
    {
        Repo("found");
        var nested = _folder.Combine("scan", "app", "node_modules", "pkg");
        Directory.CreateDirectory(Path.Combine(nested, ".git"));
        Directory.CreateDirectory(Path.Combine(_folder.Combine("scan", ".hidden", "repo"), ".git"));
        var target = _folder.Combine("elsewhere", "linked");
        Directory.CreateDirectory(Path.Combine(target, ".git"));
        if (OperatingSystem.IsWindows())
        {
            TestFolder.Junction(_folder.Combine("scan", "link"), _folder.Combine("elsewhere"));
        }

        Assert.Equal(["found"], RepoScanner.Find(_folder.Combine("scan")).Select(Path.GetFileName));
    }

    /// <summary>The scan itself never starts git: a git that fails the test on any call.</summary>
    [Fact]
    public async Task Scanning_starts_no_git_and_leaves_out_what_is_on_the_board()
    {
        Repo("one");
        Repo("two", "[core]\n\tfsmonitor = calc\n");
        var time = new FakeTimeProvider();
        var board = new BoardService(new GitClient(new NoGit()), new ConfigStore(_folder.Combine("config.json"), time), time);

        var found = await board.ScanAsync(_folder.Combine("scan"), TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two"], found.Select(c => Path.GetFileName(c.Path)).Order());
    }

    private sealed class NoGit : GitRunner
    {
        public override Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, GitCallMode mode, TimeSpan timeout,
            string? input, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"git {string.Join(' ', args)} ran in {workingDirectory} before the user ticked it.");
    }

    public void Dispose() => _folder.Dispose();
}
