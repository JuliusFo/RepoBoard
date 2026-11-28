using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith("hint:", StringComparison.Ordinal));
            return GitSecrets.Redact(string.Join(Environment.NewLine, lines));
        }
    }

    internal static GitResult Failed(string message) => new(-1, "", message);
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

        foreach (var (name, value) in EnvironmentFor(mode))
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        // Process.Start fails the same way for a missing folder as for a missing git (review 0.4.0, A17).
        if (!Directory.Exists(workingDirectory))
        {
            return GitResult.Failed(TextFormat.Format(SyncText.FolderMissing, workingDirectory));
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

            // Only the command: the arguments may hold a whole commit message (review 0.4.0, A21).
            return GitResult.Failed(TextFormat.Format(SyncText.GitTimedOut, Command(args), (int)timeout.TotalSeconds));
        }
    }

    /// <summary>The git command of <paramref name="args"/> (<c>commit</c>): the first word that is neither an option nor a value of <c>-c</c>.</summary>
    internal static string Command(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-c")
            {
                i++;
            }
            else if (!args[i].StartsWith('-'))
            {
                return args[i];
            }
        }

        return "";
    }

    /// <summary>
    /// What every call sets in git's environment (null removes a variable). Never a console prompt (there is no console);
    /// <c>status</c> takes no optional index lock, so it never collides with the IDE or a terminal working in the same
    /// repository. In the background never a sign-in window or askpass program – an empty <c>GIT_ASKPASS</c> also keeps
    /// git from falling back to <c>core.askPass</c>. What a parent process set for its own repository (a hook's
    /// <c>GIT_DIR</c>/<c>GIT_INDEX_FILE</c>, the askpass of the VS Code terminal RepoBoard was started from) never reaches
    /// git: every row would work on that one repository, or ask through a window that is not there (review 0.4.0, A20).
    /// </summary>
    public static IReadOnlyList<(string Name, string? Value)> EnvironmentFor(GitCallMode mode) =>
    [
        ("GIT_TERMINAL_PROMPT", "0"),
        ("GIT_OPTIONAL_LOCKS", "0"),
        .. Inherited.Select(name => (name, (string?)null)),
        .. mode == GitCallMode.Background
            ? [("GCM_INTERACTIVE", "never"), ("SSH_ASKPASS_REQUIRE", "never"), ("GIT_ASKPASS", "")]
            : new (string, string?)[] { ("GIT_ASKPASS", null) },
    ];

    /// <summary>git variables that point a call at another repository or add configuration – a parent's, not RepoBoard's.</summary>
    internal static readonly string[] Inherited =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_NAMESPACE", "GIT_PREFIX", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
    ];

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
