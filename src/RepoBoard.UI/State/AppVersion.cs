using System.Reflection;

namespace RepoBoard.UI.State;

/// <summary>The running version as released (<c>0.2.0</c>), from <c>Directory.Build.props</c>; without the build's commit.</summary>
public static class AppVersion
{
    public static string Current { get; } = Of(typeof(AppVersion).Assembly);

    internal static string Of(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "?";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
