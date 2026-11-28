using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Tests.Git;

/// <summary>The one place git starts (ADR 0002): what it puts in git's environment, its time limit, and its failures (review 0.4.0, C2).</summary>
public sealed class GitRunnerTests : IDisposable
{
    private readonly GitSandbox _git = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Environment(GitCallMode mode) =>
        GitRunner.EnvironmentFor(mode).ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);

    /// <summary>CLAUDE.md §2: what runs in the background never asks for credentials – it fails and says so once.</summary>
    [Fact]
    public void The_background_never_asks_and_a_click_may()
    {
        var background = Environment(GitCallMode.Background);
        Assert.Equal("never", background["GCM_INTERACTIVE"]);
        Assert.Equal("never", background["SSH_ASKPASS_REQUIRE"]);
        Assert.Equal("", background["GIT_ASKPASS"]); // empty: git does not fall back to core.askPass
        Assert.Equal("0", background["GIT_TERMINAL_PROMPT"]);

        var click = Environment(GitCallMode.Interactive);
        Assert.False(click.ContainsKey("GCM_INTERACTIVE"));
        Assert.Equal("0", click["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("0", click["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal(":", click["GIT_EDITOR"]); // "--continue" never waits for an editor (WP-07)
    }

    /// <summary>Review 0.4.0, A20: a hook's or terminal's git variables point every call at their repository.</summary>
    [Theory]
    [InlineData(GitCallMode.Background)]
    [InlineData(GitCallMode.Interactive)]
    public void A_parent_s_repository_never_reaches_git(GitCallMode mode)
    {
        var environment = Environment(mode);

        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" })
        {
            Assert.True(environment.TryGetValue(name, out var value) && value is null, name);
        }
    }

    /// <summary>What <see cref="GitRunner.EnvironmentFor"/> says arrives in git – an empty variable included.</summary>
    [Fact]
    public async Task The_environment_reaches_git()
    {
        var result = await new GitRunner().RunAsync(_git.Mine, ["-c", "alias.env=!env", "env"], GitCallMode.Background,
            GitRunner.LocalTimeout, null, Ct);

        Assert.True(result.Success, result.Error);
        var lines = result.Output.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains("GIT_ASKPASS=", lines);
        Assert.Contains("GCM_INTERACTIVE=never", lines);
    }

    /// <summary>Review 0.4.0, A21: the message names the command, not its arguments – they may hold a whole commit message.</summary>
    [Fact]
    public async Task A_call_without_an_answer_is_cut_off_and_names_its_command()
    {
        var started = DateTime.UtcNow;
        var result = await new GitRunner().RunAsync(_git.Mine, ["-c", "alias.wait=!sleep 20", "wait"],
            GitCallMode.Interactive, TimeSpan.FromMilliseconds(300), null, Ct);

        Assert.False(result.Success);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
        Assert.StartsWith("git wait:", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "commit", "-m", "text" }, "commit")]
    [InlineData(new[] { "--literal-pathspecs", "add", "-A", "--", "a" }, "add")]
    [InlineData(new[] { "-c", "x=y", "status" }, "status")]
    public void The_command_is_the_first_word_that_is_no_option(string[] args, string command) =>
        Assert.Equal(command, GitRunner.Command(args));

    [Fact]
    public async Task A_missing_git_and_a_missing_folder_say_which()
    {
        var noGit = await new GitRunner("git-that-does-not-exist").RunAsync(_git.Mine, ["status"], GitCallMode.Interactive,
            GitRunner.LocalTimeout, null, Ct);
        Assert.Equal(SyncText.GitMissing, noGit.Message);

        var gone = Path.Combine(_git.Root, "gone");
        var noFolder = await new GitRunner().RunAsync(gone, ["status"], GitCallMode.Interactive, GitRunner.LocalTimeout, null, Ct);
        Assert.Equal(TextFormat.Format(SyncText.FolderMissing, gone), noFolder.Message); // review 0.4.0, A17: not "git was not found"
    }

    public void Dispose() => _git.Dispose();
}
