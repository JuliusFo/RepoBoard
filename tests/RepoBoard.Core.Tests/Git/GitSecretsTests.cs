using RepoBoard.Core.Git;

namespace RepoBoard.Core.Tests.Git;

public sealed class GitSecretsTests
{
    // Built at run time, so no token-shaped literal sits in the repository (secret scanning).
    private static readonly string GitHubToken = "ghp_" + new string('A', 36);
    private static readonly string FineGrainedToken = "github_pat_" + new string('B', 40);
    private static readonly string GitLabToken = "glpat-" + new string('C', 20);

    public static TheoryData<string, string> Cases => new()
    {
        { "fatal: unable to access 'https://jane:s3cr%40t@github.com/x/y.git/'", "fatal: unable to access 'https://jane:***@github.com/x/y.git/'" },
        { "fatal: unable to access 'https://jane:@github.com/x.git/'", "fatal: unable to access 'https://jane:***@github.com/x.git/'" },
        { "From HTTPS://abc123@dev.azure.com/org/repo", "From HTTPS://***@dev.azure.com/org/repo" },
        { $"remote: invalid token {GitHubToken} for x", "remote: invalid token *** for x" },
        { $"remote: {FineGrainedToken} / {GitLabToken}", "remote: *** / ***" },
        // Not secrets: SSH user names, scp-like remotes, e-mail addresses, plain URLs.
        { "ssh://git@github.com/x/y.git", "ssh://git@github.com/x/y.git" },
        { "git@github.com: Permission denied (publickey).", "git@github.com: Permission denied (publickey)." },
        { "Author jane@example.com, see https://github.com/x/y", "Author jane@example.com, see https://github.com/x/y" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Redact_masks_credentials_in_urls_and_known_tokens(string text, string expected) =>
        Assert.Equal(expected, GitSecrets.Redact(text));

    [Fact]
    public void The_message_shown_to_the_user_is_masked()
    {
        // A progress line ends with \r and the next message follows on it, as a terminal overwrites it (E2E WP-07); with TERM
        // set, git clears the line with ESC[K first (second review pass WP-07).
        var result = new GitResult(128, "", "hint: ignored\r\nRebasing (1/2)\r\u001b[Kfatal: unable to access 'https://jane:secret@example.com/r.git/': 403\r\n");

        Assert.Equal("fatal: unable to access 'https://jane:***@example.com/r.git/': 403", result.Message);
    }
}
