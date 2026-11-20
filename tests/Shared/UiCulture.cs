using System.Globalization;
using System.Runtime.CompilerServices;

namespace RepoBoard.Testing;

/// <summary>
/// The UI language of the tests: German on every machine, also on the English CI runners – a test that checks a text must
/// not depend on the machine (from FerretSharp WP-29). A test of the English texts switches with <see cref="Use"/>.
/// </summary>
internal static class UiCulture
{
    [ModuleInitializer]
    internal static void UseGermanByDefault() =>
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

    /// <summary>Switches the UI culture of the current test until disposed (it flows with the test's async context only).</summary>
    public static IDisposable Use(string name)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
