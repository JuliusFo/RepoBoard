<#
.SYNOPSIS
    Measures how long a hover or a click takes to show in the window of a test instance - what the user sees, not what
    the page did (ADR 0008).

.DESCRIPTION
    BlazorWebView shows the page as a captured picture; a frame WPF does not show stays unseen until the next change of
    the page. This script moves the mouse alternately onto the first and fifth repository row (hover), or with -Click
    clicks the first row (opens and closes it), always through Invoke-RealClick.ps1. Meanwhile one thread polls the cursor
    (or the left button) every millisecond and another takes the window picture as fast as it comes (PrintWindow with
    PW_RENDERFULLCONTENT: what DWM has, about every 16.7 ms at 60 Hz) and watches a strip of the row.

    Per move or click: the time from entering the row (releasing the button) to the first changed picture; -1 if nothing
    changed until the next move or click - the frame was lost. Before the fix of ADR 0008 (1440x900, 2026-11-21) half of
    the hovers and a quarter of the clicks gave -1.

    Needs at least five repositories in the test instance's config.json; leave the mouse alone while it runs. Clicks need
    unfiltered injected input (Test-InjectedInput.ps1); hover works without.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Measure-PictureDelay.ps1 -Root <scratch>\e2e -ProcessId 12345

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/e2e/Measure-PictureDelay.ps1 -Root <scratch>\e2e -ProcessId 12345 -Port 9334 -Click -Runs 40
#>
param(
    # The folder Start-TestInstance.ps1 was given.
    [Parameter(Mandatory)][string]$Root,
    # Process id of the test instance (Start-TestInstance.ps1).
    [Parameter(Mandatory)][int]$ProcessId,
    [int]$Port = 9333,
    [int]$Runs = 30,
    # Clicks the first row instead of hovering; measured at the place of the second row, where the details appear.
    [switch]$Click
)
$ErrorActionPreference = 'Stop'
$src = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
public static class PictureProbe {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

  static IntPtr _window; static Rectangle[] _strips; static Rectangle[] _rows; static bool _click; static volatile bool _stop;
  static readonly Stopwatch Clock = Stopwatch.StartNew();
  static Thread _input, _picture;
  // "t index": the cursor entered row <index> (-1: none), or with clicks the button was released (index 1).
  public static readonly List<string> Events = new List<string>();
  // t, mean brightness of each strip
  public static readonly List<double[]> Shots = new List<double[]>();

  // Client pixels, four numbers (x, y, width, height) per rectangle.
  public static void Start(IntPtr window, int[] strips, int[] rows, bool click) {
    _window = window; _click = click; _stop = false;
    _strips = ToRects(strips); _rows = ToRects(rows);
    _input = new Thread(InputLoop) { IsBackground = true, Priority = ThreadPriority.Highest }; _input.Start();
    _picture = new Thread(PictureLoop) { IsBackground = true }; _picture.Start();
  }
  public static void Stop() { _stop = true; _input.Join(); _picture.Join(); }

  static Rectangle[] ToRects(int[] v) {
    var r = new Rectangle[v.Length / 4];
    for (int i = 0; i < r.Length; i++) r[i] = new Rectangle(v[4*i], v[4*i+1], v[4*i+2], v[4*i+3]);
    return r;
  }
  static void Note(int index) {
    lock (Events) Events.Add(Clock.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " " + index);
  }

  static void InputLoop() {
    int last = -1; bool down = false;
    while (!_stop) {
      if (_click) {
        bool now = (GetAsyncKeyState(1) & 0x8000) != 0;
        if (down && !now) Note(1);
        down = now;
      } else {
        POINT p; GetCursorPos(out p);
        var o = new POINT(); ClientToScreen(_window, ref o);
        int now = -1;
        for (int i = 0; i < _rows.Length; i++) if (_rows[i].Contains(p.X - o.X, p.Y - o.Y)) now = i;
        if (now != last) { Note(now); last = now; }
      }
      Thread.Sleep(1);
    }
  }

  static void PictureLoop() {
    RECT wr; GetWindowRect(_window, out wr);
    int w = wr.R - wr.L, h = wr.B - wr.T;
    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb)) {
      while (!_stop) {
        GetWindowRect(_window, out wr);
        var o = new POINT(); ClientToScreen(_window, ref o);
        int dx = o.X - wr.L, dy = o.Y - wr.T; // the picture includes the title bar and borders
        using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(_window, hdc, 2); g.ReleaseHdc(hdc); }
        double t = Clock.Elapsed.TotalMilliseconds;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var shot = new double[1 + _strips.Length]; shot[0] = t;
        for (int i = 0; i < _strips.Length; i++) {
          var r = _strips[i]; long sum = 0; int n = 0;
          for (int y = r.Y + 2; y < r.Bottom - 2; y += 2)
            for (int x = r.X; x < r.Right; x += 2) {
              int v = Marshal.ReadInt32(d.Scan0, (y + dy) * d.Stride + (x + dx) * 4);
              sum += (v & 0xFF) + ((v >> 8) & 0xFF) + ((v >> 16) & 0xFF); n++;
            }
          shot[1 + i] = n == 0 ? 0 : (double)sum / n;
        }
        bmp.UnlockBits(d);
        lock (Shots) Shots.Add(shot);
      }
    }
  }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing
[PictureProbe]::SetProcessDPIAware() | Out-Null

$window = (Get-Process -Id $ProcessId).MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw "Process $ProcessId has no window." }
$realClick = Join-Path $PSScriptRoot 'Invoke-RealClick.ps1'

$js = [IO.Path]::GetTempFileName()
@'
(() => {
  const rows = [...document.querySelectorAll('.row.repo')];
  if (rows.length < 5) return null;
  const rect = r => { const b = r.getBoundingClientRect(); return [b.x, b.y, b.width, b.height]; };
  return { dpr: devicePixelRatio, rows: [rect(rows[0]), rect(rows[4])], below: rect(rows[1]) };
})()
'@ | Set-Content $js -Encoding utf8
try {
    $info = (node (Join-Path $PSScriptRoot 'cdp.mjs') $Port eval $js) -join "`n" | ConvertFrom-Json
} finally { Remove-Item $js }
if (-not $info) { throw 'The test instance shows fewer than five repositories.' }
$dpr = [double]$info.dpr

# Rows (for the cursor) and strips (for the picture) in client pixels. Hover: a narrow strip in the empty middle of each
# row, whose background changes. Click: the whole place of the second row, where the opened first row's details appear.
$rows = @(); $strips = @()
foreach ($r in $info.rows) {
    $rows += [int]($r[0] * $dpr), [int]($r[1] * $dpr), [int]($r[2] * $dpr), [int]($r[3] * $dpr)
    $strips += [int](($r[0] + $r[2] * 0.35) * $dpr), [int]($r[1] * $dpr), [int]($r[2] * 0.05 * $dpr), [int]($r[3] * $dpr)
}
if ($Click) {
    for ($k = 0; $k -lt 4; $k++) { $strips[4 + $k] = [int]($info.below[$k] * $dpr) }
}
# Targets in CSS pixels: on the name at the left of the row.
$targets = $info.rows | ForEach-Object { , @(($_[0] + 30), ($_[1] + $_[3] / 2)) }

[PictureProbe]::Start($window, [int[]]$strips, [int[]]$rows, [bool]$Click)
try {
    for ($i = 0; $i -lt $Runs; $i++) {
        if ($Click) {
            $t = $targets[0]
            powershell -NoProfile -ExecutionPolicy Bypass -File $realClick -Root $Root -ProcessId $ProcessId -X $t[0] -Y $t[1] -Dpr $dpr | Out-Null
        } else {
            $t = $targets[$i % 2]
            powershell -NoProfile -ExecutionPolicy Bypass -File $realClick -Root $Root -ProcessId $ProcessId -X $t[0] -Y $t[1] -Dpr $dpr -MoveOnly | Out-Null
        }
        if ($LASTEXITCODE -ne 0) { throw 'Invoke-RealClick.ps1 stopped (see its message) - measurement ended.' }
        Start-Sleep -Milliseconds (400 + (Get-Random -Maximum 400))
    }
} finally { [PictureProbe]::Stop() }

$shots = [PictureProbe]::Shots
$events = [PictureProbe]::Events | ForEach-Object {
    $p = $_ -split ' '
    [pscustomobject]@{ T = [double]::Parse($p[0], [Globalization.CultureInfo]::InvariantCulture); Index = [int]$p[1] }
}
$results = @()
for ($e = 0; $e -lt $events.Count; $e++) {
    $ev = $events[$e]
    if ($ev.Index -lt 0) { continue }
    $next = if ($e + 1 -lt $events.Count) { $events[$e + 1].T } else { [double]::MaxValue }
    $before = $shots | Where-Object { $_[0] -lt $ev.T } | Select-Object -Last 1
    if (-not $before) { continue }
    $changed = $shots | Where-Object { $_[0] -ge $ev.T -and $_[0] -lt $next -and [Math]::Abs($_[1 + $ev.Index] - $before[1 + $ev.Index]) -gt 3 } |
        Select-Object -First 1
    $results += if ($changed) { [Math]::Round($changed[0] - $ev.T) } else { -1 }
}
$shown = @($results | Where-Object { $_ -ge 0 } | Sort-Object)
$gaps = for ($k = 1; $k -lt $shots.Count; $k++) { $shots[$k][0] - $shots[$k - 1][0] }
[pscustomobject]@{
    Kind = $(if ($Click) { 'click' } else { 'hover' })
    Count = $results.Count
    Lost = @($results | Where-Object { $_ -lt 0 }).Count
    Over100ms = @($shown | Where-Object { $_ -gt 100 }).Count
    MedianMs = $(if ($shown.Count) { $shown[[int]($shown.Count / 2)] })
    MaxMs = $(if ($shown.Count) { $shown[-1] })
    PictureEveryMs = [Math]::Round(($gaps | Measure-Object -Average).Average, 1)
    All = $results -join ' '
}
