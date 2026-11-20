namespace RepoBoard.Core;

/// <summary>The project on GitHub: where updates come from (WP-10) and where the releases are listed.</summary>
public static class ProjectSite
{
    public const string Owner = "JuliusFo";

    public const string Repository = "RepoBoard";

    public const string Url = "https://github.com/" + Owner + "/" + Repository;

    public const string Releases = Url + "/releases";
}
