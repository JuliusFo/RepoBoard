using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Git;

/// <summary>Result of one git call.</summary>
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;

    /// <summary>
    /// What to show the user: git writes progress and errors to stderr, results to stdout; hints are left out, credentials
    /// masked (<see cref="GitSecrets"/>).
    /// </summary>
    public string Message
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Error) ? Output : Error;
            // Of a line git rewrote with \r (progress: "Rebasing (1/2)\rerror: …") only the last part, as a terminal shows it.
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Select(l => Csi.Replace(l[(l.LastIndexOf('\r') + 1)..], "").Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("hint:", StringComparison.Ordinal));
            return GitSecrets.Redact(string.Join(Environment.NewLine, lines));
        }
    }

    internal static GitResult Failed(string message) => new(-1, "", message);

    /// <summary>
    /// A terminal control sequence: with TERM set, git clears a progress line with <c>\r ESC[K</c> before the next message
    /// (second review pass WP-07).
    /// </summary>
    private static readonly Regex Csi = new(@"\x1b\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
}

/// <summary>How a call may ask for credentials (ADR 0003).</summary>
public enum GitCallMode
{
    /// <summary>Started by a click: the Git Credential Manager may show its sign-in window.</summary>
    Interactive,

    /// <summary>Auto-sync: never a prompt – a missing credential fails the call instead of waiting for nobody.</summary>
    Background,
}

/// <summary>
/// The only place RepoBoard starts git (ADR 0002, <c>ArchitectureTests</c>): the installed CLI, so the user's credential
/// helper, SSH config, hooks, LFS and includes behave exactly as in their terminal and IDE. Arguments only through
/// <see cref="ProcessStartInfo.ArgumentList"/>, never a shell; every call has a time limit. Standard input is empty unless a
/// call passes <c>input</c> (a patch for <c>git apply</c>, WP-25).
/// </summary>
public class GitRunner(string executable = "git")
{
    public static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// For local commands that write commits and may sign each of them – merge, rebase, continue (WP-07): a gpg passphrase
    /// in pinentry must not run into <see cref="LocalTimeout"/>, which would end git halfway through.
    /// </summary>
    public static readonly TimeSpan CommitTimeout = TimeSpan.FromMinutes(3);

    public virtual async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, GitCallMode mode,
        TimeSpan timeout, string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // git reads a patch as bytes: a BOM would be part of its first line.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        // Paths unquoted (umlauts), no colour codes in the output.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("color.ui=false");
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // Never a console prompt (there is no console); status takes no optional index lock, so it never collides with the
        // IDE or a terminal working in the same repository. Never an editor either (WP-07): "--continue" would open one for
        // the message and wait for nobody to close it. ":" is git's own "no editor" – it starts no process and keeps the
        // message git prepared, also where no "true" program exists (MinGit; review WP-07).
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_EDITOR"] = ":";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        if (mode == GitCallMode.Background)
        {
            start.Environment["GCM_INTERACTIVE"] = "never";
            start.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;

        Process? started;
        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return GitResult.Failed(SyncText.GitMissing);
        }

        using var process = started ?? throw new InvalidOperationException("git could not be started.");
        // Read before writing: git may answer while it still reads, and full pipes on both sides would wait for each other.
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await WriteInputAsync(process, input, token);
            await process.WaitForExitAsync(token);
            return new GitResult(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return GitResult.Failed(TextFormat.Format(SyncText.GitTimedOut, string.Join(' ', args), (int)timeout.TotalSeconds));
        }
    }

    private static async Task WriteInputAsync(Process process, string? input, CancellationToken token)
    {
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), token);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // git ended before it read everything (it refused the call): its exit code and stderr say why.
        }
    }
}
