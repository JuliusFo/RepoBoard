<#
.SYNOPSIS
    Drives the notification-area icon of a test instance without injected input (filtered on some machines, see
    docs/e2e-testing.md): the callback message Windows sends to NotifyIcon's hidden window, then the menu through UI
    Automation.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Invoke-TrayIcon.ps1 -Root <scratch>\e2e -ProcessId <id> -Click menu -Item 'Beenden'

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Invoke-TrayIcon.ps1 -Root <scratch>\e2e -ProcessId <id> -Click left
#>
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][int]$ProcessId,
    # menu: right click (opens the menu, lists its items); left: left click (shows the window).
    [ValidateSet('menu', 'left')][string]$Click = 'menu',
    # The menu item to invoke (its text in the UI language), for example 'Auto-Sync pausieren'.
    [string]$Item
)
$ErrorActionPreference = 'Stop'
$app = (Join-Path ([IO.Path]::GetFullPath($Root)) 'app').TrimEnd('\') + '\'
$process = Get-Process -Id $ProcessId
if (-not $process.Path -or -not $process.Path.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Process $ProcessId ($($process.Path)) is not a test instance below $app."
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace E2E -Name Tray -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
'@

# NotifyIcon's window: a hidden WinForms window without a title (the main window is WPF and titled "RepoBoard").
$targets = New-Object System.Collections.Generic.List[IntPtr]
[E2E.Tray]::EnumWindows({ param($h, $l)
    $p = 0; [void][E2E.Tray]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $ProcessId) {
        $c = New-Object System.Text.StringBuilder 256; [void][E2E.Tray]::GetClassName($h, $c, 256)
        $t = New-Object System.Text.StringBuilder 256; [void][E2E.Tray]::GetWindowText($h, $t, 256)
        if ($c.ToString().StartsWith('WindowsForms10.Window') -and $t.Length -eq 0) { $targets.Add($h) }
    }
    $true }, [IntPtr]::Zero) | Out-Null
if ($targets.Count -eq 0) { throw "Process $ProcessId has no notification-area icon window." }

$callback = 0x0400 + 1024 # NotifyIcon's WM_TRAYMOUSEMESSAGE; lParam is the mouse message
foreach ($h in $targets) {
    if ($Click -eq 'menu') { [void][E2E.Tray]::PostMessage($h, $callback, [IntPtr]::Zero, [IntPtr]0x0205) } # WM_RBUTTONUP
    else {
        [void][E2E.Tray]::PostMessage($h, $callback, [IntPtr]::Zero, [IntPtr]0x0201) # WM_LBUTTONDOWN
        [void][E2E.Tray]::PostMessage($h, $callback, [IntPtr]::Zero, [IntPtr]0x0202) # WM_LBUTTONUP
    }
}
if ($Click -ne 'menu') { return }

Start-Sleep -Milliseconds 700
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$condition = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem))
$items = $desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
"menu: $(@($items | ForEach-Object { $_.Current.Name }) -join ' | ')"
if ($Item) {
    $target = $items | Where-Object { $_.Current.Name -eq $Item } | Select-Object -First 1
    if (-not $target) { throw "No menu item '$Item'." }
    $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    "invoked: $Item"
}
