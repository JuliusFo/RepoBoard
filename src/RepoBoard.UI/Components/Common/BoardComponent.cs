using Microsoft.AspNetCore.Components;
using RepoBoard.Core.Board;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Components;

/// <summary>
/// Base of the board's components (FerretSharp's <c>FerretComponent</c>): the board and the host, and a guard for work –
/// an exception goes to the activity log with its message instead of ending the page (Blazor's error bar), a
/// cancellation stays silent. Work that does not start in a Blazor event handler (timer, service event) goes through
/// <see cref="Fire(Func{Task})"/>, never <c>_ = …</c>.
/// </summary>
public abstract class BoardComponent : ComponentBase
{
    [Inject] protected BoardService Board { get; set; } = null!;
    [Inject] protected IDesktop Desktop { get; set; } = null!;

    /// <summary>Starts <paramref name="work"/> on the renderer's context without waiting for it.</summary>
    protected void Fire(Func<Task> work) => _ = InvokeAsync(() => GuardAsync(work));

    /// <summary>Runs <paramref name="work"/>; an exception is reported on the board, not thrown at the caller.</summary>
    protected async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Board.ReportError(Core.AppPaths.AppFolderName, ex.Message);
        }
    }

    /// <inheritdoc cref="GuardAsync(Func{Task})"/>
    protected void Guard(Action work)
    {
        try
        {
            work();
        }
        catch (OperationCanceledException)
        {
            // as in GuardAsync: a cancellation is no error (review 0.4.0, A32)
        }
        catch (Exception ex)
        {
            Board.ReportError(Core.AppPaths.AppFolderName, ex.Message);
        }
    }

    /// <summary>
    /// Moves the focus, quietly: an element a new status took away in the same render is gone from the page, and focusing
    /// it threw – Blazor's error bar for a nicety (review 0.4.0, A27). Dialogs take the focus when they open, so Esc
    /// closes them (A30).
    /// </summary>
    protected static async Task FocusQuietlyAsync(Func<ValueTask> focus)
    {
        try
        {
            await focus();
        }
        catch (Microsoft.JSInterop.JSException)
        {
        }
    }

    /// <inheritdoc cref="FocusQuietlyAsync(Func{ValueTask})"/>
    protected static Task FocusQuietlyAsync(ElementReference element) => FocusQuietlyAsync(() => element.FocusAsync());
}
