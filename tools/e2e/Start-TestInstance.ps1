<#
.SYNOPSIS
    Builds RepoBoard into a scratch folder and starts a test instance on its own data and WebView2 folders with a
    debug port for CDP (tools/e2e/cdp.mjs).

.DESCRIPTION
    Everything lives below -Root: app\ (build output), data\ (--data-dir, log in data\logs) and webview2\. The user's
    data, WebView2 folder and a running instance are not touched; the build does not go to bin\Debug, which a running
    instance of the user would lock.

    Refuses a debug port that is already taken (another session's instance would get every CDP call) and checks
    afterwards that the port belongs to the new instance. The window is placed at 100,100 with 1440x900 on the
    primary screen without taking the focus, so clicks never land on another screen.

    Prints the process id; close the instance with Stop-TestInstance.ps1, never all RepoBoard processes.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Start-TestInstance.ps1 -Root <scratch>\e2e -Lang de

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Start-TestInstance.ps1 -Root <scratch>\e2e -Port 9334 -NoBuild

.EXAMPLE
    # A portable Velopack test version unzipped into <Root>\app (WP-10, docs/e2e-testing.md "Updates").
    powershell -ExecutionPolicy Bypass -File tools/e2e/Start-TestInstance.ps1 -Root <scratch>\e2e -NoBuild -Exe current\RepoBoard.exe -AppArgs '--update-source=<scratch>\feed'
#>
param(
    # Scratch folder for build, data and WebView2 (for example in the session's scratchpad).
    [Parameter(Mandatory)][string]$Root,
    [int]$Port = 9333,
    [ValidateSet('en', 'de')][string]$Lang = 'en',
    [ValidateSet('dark', 'light')][string]$Theme = 'dark',
    # Starts the build already in <Root>\app.
    [switch]$NoBuild,
    # The program below <Root>\app: current\RepoBoard.exe for an unzipped portable Velopack version (WP-10).
    [string]$Exe = 'RepoBoard.exe',
    # More arguments for the app, for example a repoboard:// link or --import-appdata=<scratch>.
    [string[]]$AppArgs = @()
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$Root = [IO.Path]::GetFullPath($Root)
$app = Join-Path $Root 'app'
$data = Join-Path $Root 'data'
$webView = Join-Path $Root 'webview2'
New-Item -ItemType Directory -Force $app, $data, $webView | Out-Null

# Asked over HTTP as well: some machines list no listeners (WP-02 E2E), and an answer here means another instance.
$answers = try { [bool](Invoke-RestMethod "http://127.0.0.1:$Port/json/version" -TimeoutSec 2) } catch { $false }
if ($answers -or (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)) {
    throw "Port $Port is taken (another test instance?). Use -Port with a free port."
}

if (-not $NoBuild) {
    dotnet build (Join-Path $repo 'src\RepoBoard.App') -o $app -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$saved = @{
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    WEBVIEW2_USER_DATA_FOLDER = $env:WEBVIEW2_USER_DATA_FOLDER
}
try {
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
    $env:WEBVIEW2_USER_DATA_FOLDER = $webView
    $arguments = @("--data-dir=`"$data`"", "--lang=$Lang", "--theme=$Theme") + $AppArgs
    $process = Start-Process (Join-Path $app $Exe) -ArgumentList $arguments -PassThru
} finally {
    foreach ($name in $saved.Keys) { Set-Item "env:$name" -Value $saved[$name] }
}

$ready = $false
for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
    Start-Sleep -Milliseconds 500
    if ($process.HasExited) { throw "The instance exited with code $($process.ExitCode); see $data\logs." }
    try {
        $targets = Invoke-RestMethod "http://127.0.0.1:$Port/json" -TimeoutSec 2
        $ready = [bool]($targets | Where-Object url -eq 'https://0.0.0.1/')
    } catch { }
}
if (-not $ready) { throw "No RepoBoard page on port $Port after 30 s." }

$owner = (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).OwningProcess | Select-Object -First 1
if ($owner) {
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$owner").ParentProcessId
    $ours = $parent -eq $process.Id
} else {
    # Some machines list no listener at all (WP-02 E2E: neither the CIM query nor netstat, while CDP works). The port was
    # free before the start (HTTP check above), so what answers now is the new instance – if its own WebView2 browser
    # process was started with this port (Review WP-02: WebView2 writes no DevToolsActivePort to compare with).
    $ours = [bool](Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" |
        Where-Object { $_.CommandLine -match "--remote-debugging-port=$Port(\s|$)" })
    $owner = 'unknown'
}
if (-not $ours) {
    Stop-Process -Id $process.Id
    throw "Port $Port belongs to process $owner, not to the new instance - stopped it."
}

Add-Type -Namespace E2E -Name Window -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);'
$process.Refresh()
for ($i = 0; $i -lt 20 -and $process.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $process.Refresh() }
[E2E.Window]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, 100, 100, 1440, 900, 0x0014) | Out-Null # NOZORDER | NOACTIVATE

[pscustomobject]@{ ProcessId = $process.Id; Port = $Port; DataDir = $data; Log = Join-Path $data 'logs' }
