using Microsoft.AspNetCore.Components;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Components;

/// <summary>
/// A component that shows the update state (WP-10): it renders on every change of <see cref="UpdateState"/>, which comes
/// from the thread pool, and lets go of it when it goes. Three components had the same subscription (review 0.4.0, B7).
/// </summary>
public abstract class UpdateComponent : BoardComponent, IDisposable
{
    [Inject] protected UpdateState Updates { get; set; } = null!;

    protected override void OnInitialized() => Updates.Changed += OnUpdateChanged;

    private void OnUpdateChanged() => Fire(() =>
    {
        StateHasChanged();
        return Task.CompletedTask;
    });

    public void Dispose()
    {
        Updates.Changed -= OnUpdateChanged;
        GC.SuppressFinalize(this);
    }
}
