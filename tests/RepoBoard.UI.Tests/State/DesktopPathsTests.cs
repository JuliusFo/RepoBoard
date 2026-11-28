using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>The host's decisions about paths, tested without the host (review 0.4.0, A34, A35, C9).</summary>
public sealed class DesktopPathsTests : IDisposable
{
    private readonly TestFolder _folder = new();

    [Fact]
    public void The_folder_dialog_starts_where_the_repositories_are()
    {
        var src = _folder.Combine("src");

        Assert.Equal(src, DesktopPaths.StartFolder([Path.Combine(src, "a"), Path.Combine(src, "b")]));
        Assert.Equal(src, DesktopPaths.StartFolder([Path.Combine(src, "a"), Path.Combine(src, "group", "b")]));
        Assert.Equal(src, DesktopPaths.StartFolder([Path.Combine(src, "a") + Path.DirectorySeparatorChar]));
        Assert.Null(DesktopPaths.StartFolder([]));
    }

    [Theory]
    [InlineData("C:\\src\\repo", true)]
    [InlineData("C:\\src\\my repo (old)", true)]
    [InlineData("C:\\src\\a&calc", false)]
    [InlineData("C:\\src\\a|b", false)]
    [InlineData("C:\\src\\%PATH%", false)]
    [InlineData("C:\\src\\a^b", false)]
    [InlineData("C:\\src\\\"quoted\"", false)]
    public void Only_a_path_cmd_passes_on_as_it_is_goes_to_cmd(string path, bool safe) =>
        Assert.Equal(safe, DesktopPaths.SafeForCmd(path));

    [Fact]
    public void A_command_is_found_on_the_path_with_its_extension_or_not_at_all()
    {
        var bin = _folder.Combine("bin");
        Directory.CreateDirectory(bin);
        var name = OperatingSystem.IsWindows() ? "code.cmd" : "code";
        File.WriteAllText(Path.Combine(bin, name), "");
        var path = string.Join(Path.PathSeparator, _folder.Combine("empty"), bin);

        Assert.Equal(Path.Combine(bin, name), DesktopPaths.FindOnPath("code", path, ".EXE;.CMD"), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(DesktopPaths.FindOnPath("code", _folder.Combine("empty"), ".EXE;.CMD"));
        Assert.Null(DesktopPaths.FindOnPath("code", null, null));
    }

    public void Dispose() => _folder.Dispose();
}
