namespace RepoBoard.Testing;

/// <summary>The repository the tests run in, for tests that read its files (code, scripts, changelog). Linked into every test project.</summary>
internal static class Repository
{
    /// <summary>The folder with <c>RepoBoard.slnx</c>, above the test's output folder.</summary>
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RepoBoard.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("RepoBoard.slnx not found above " + AppContext.BaseDirectory);
    }
}
