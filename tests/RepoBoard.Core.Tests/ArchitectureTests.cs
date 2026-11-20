using System.Text.RegularExpressions;

namespace RepoBoard.Core.Tests;

/// <summary>Tripwires for the layer rules of CLAUDE.md §4.</summary>
public sealed partial class ArchitectureTests
{
    private static readonly string[] ForbiddenAssemblies = ["PresentationCore", "PresentationFramework", "WindowsBase", "System.Windows.Forms"];

    [Fact]
    public void Core_does_not_reference_ui_assemblies()
    {
        var referenced = typeof(AppPaths).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(referenced, name => ForbiddenAssemblies.Contains(name));
    }

    /// <summary>
    /// Processes start only in <c>GitRunner</c> (ADR 0002: time limit, argument list, no shell, no prompt) and in the host
    /// (Explorer, terminal, editor). A start anywhere else would bypass those rules.
    /// </summary>
    [Fact]
    public void Processes_start_only_in_the_git_runner_and_the_host()
    {
        var root = Repository.Root;
        var allowed = new[] { "src/RepoBoard.Core/Git/GitRunner.cs", "src/RepoBoard.App/" };
        var found = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Select(f => (File: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(f => !f.Relative.Contains("/obj/", StringComparison.Ordinal) && !f.Relative.Contains("/bin/", StringComparison.Ordinal)
                        && !allowed.Any(a => f.Relative.StartsWith(a, StringComparison.Ordinal)))
            .SelectMany(f => File.ReadLines(f.File).Where(line => ProcessStart().IsMatch(line)).Select(line => $"{f.Relative}: {line.Trim()}"))
            .ToList();

        Assert.Empty(found);
    }

    [GeneratedRegex(@"\bProcess\.Start\s*\(|\bnew\s+ProcessStartInfo\b|\bnew\s+Process\s*\(")]
    private static partial Regex ProcessStart();
}
