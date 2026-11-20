namespace RepoBoard.UI.State;

/// <summary>Start with Windows (ADR 0006): only the host can read and write the entry. Implemented in the App.</summary>
public interface IAutostart
{
    /// <summary>The entry is there and not switched off in the Task Manager – read each time, the user can change it there.</summary>
    bool IsOn { get; }

    void Set(bool on);
}
