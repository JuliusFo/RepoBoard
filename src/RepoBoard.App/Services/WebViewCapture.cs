using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using Windows.Foundation;
using Windows.Graphics.Capture;

namespace RepoBoard.App.Services;

/// <summary>
/// Shows each frame of the WebView once it is really there (ADR 0008, the frame part of FerretSharp's <c>WebViewCapture</c>,
/// ADR 0038/0039 there). BlazorWebView shows the page through the <see cref="WebView2CompositionControl"/>, which captures
/// the browser's output (<c>GraphicsCaptureSession</c>, a frame pool of one buffer) and copies each frame into a
/// <see cref="D3DImage"/>. The capture delivers a frame only when the page changes, so a frame WPF does not show stays
/// unseen until the next change – a hover effect or the result of a click showed late (2026-11-21). Two ways to lose one:
/// <list type="bullet">
/// <item>The copy runs on the GPU and WPF presents the shared texture from its own device without waiting for it: a copy not
/// yet done shows the previous picture. Here the UI thread waits for it after each copy (<see cref="CopyFence"/>).</item>
/// <item>The control copies a frame only if it can lock the image at once (<c>TryLock</c> without waiting) and drops it
/// otherwise – while WPF's render thread still presents the previous one. Here the frame waits in the pool until the image
/// is free and is copied then.</item>
/// </list>
/// Both need the control's frame handler replaced by this one. Its members are private, reached by reflection and checked
/// once the WebView exists – should a WebView2 update rename them, the log says so and the control works as it does on
/// its own. Not ported: the restart of the capture after sleep, a session switch or a lost front buffer (backlog).
/// </summary>
public sealed class WebViewCapture(ILogger<WebViewCapture> logger)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>The control's <c>GraphicsItemD3DImage</c>; created when the control is loaded.</summary>
    private static readonly FieldInfo? ImageField = typeof(WebView2CompositionControl).GetField("_d3dImage", Private);

    /// <summary>Its frame pool (new with every rebuild), its handler of a frame and the copy of the next frame into the image.</summary>
    private static readonly FieldInfo? PoolField = ImageField?.FieldType.GetField("_framePool", Private);
    private static readonly MethodInfo? FrameArrived = ImageField?.FieldType.GetMethod("OnFrameArrived", Private, [typeof(Direct3D11CaptureFramePool), typeof(object)]);
    private static readonly MethodInfo? RequestRender = ImageField?.FieldType.GetMethod("RequestRender", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);

    /// <summary>Its Direct3D 11 device and context (new with every rebuild), which do the copy.</summary>
    private static readonly FieldInfo? DeviceField = ImageField?.FieldType.GetField("_d3d11Device", Private);
    private static readonly FieldInfo? ContextField = ImageField?.FieldType.GetField("_d3d11DeviceContext", Private);

    /// <summary>How often a frame that waits checks the image again, and for how long at most (then copied or dropped as before).</summary>
    private static readonly TimeSpan HoldStep = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The control rebuilds devices, capture and pool after a change of the display settings (WebView2 1.0.4258), at a time
    /// of its choosing: a new pool is looked for this often.
    /// </summary>
    private static readonly TimeSpan PoolCheck = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the UI thread waits at most for the GPU's copy of a frame (then WPF may show the previous picture, as
    /// before); and from how long on a wait is noted in the log.
    /// </summary>
    private static readonly TimeSpan CopyLimit = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CopyNoted = TimeSpan.FromMilliseconds(2);

    /// <summary>Held frames, long copies and arrived frames are noted in the log at most this often.</summary>
    private static readonly TimeSpan NoteEvery = TimeSpan.FromMinutes(1);

    /// <summary>The private members not found in this WebView2 version (tested against the pinned one).</summary>
    internal static IReadOnlyList<string> MissingMembers() =>
    [
        .. new (string Name, MemberInfo? Member)[]
        {
            ("WebView2CompositionControl._d3dImage", ImageField),
            ("GraphicsItemD3DImage._framePool", PoolField),
            ("GraphicsItemD3DImage.OnFrameArrived", FrameArrived),
            ("GraphicsItemD3DImage.RequestRender", RequestRender),
            ("GraphicsItemD3DImage._d3d11Device", DeviceField),
            ("GraphicsItemD3DImage._d3d11DeviceContext", ContextField),
        }.Where(m => m.Member is null).Select(m => m.Name),
    ];

    private WebView2CompositionControl? _control;

    private Direct3D11CaptureFramePool? _pool;
    private DispatcherTimer? _hold;
    private long _heldSince;
    private int _held;
    private int _dropped;
    private TimeSpan _longestHold;
    private long _lastNote;

    private int _arrived;
    private long _arrivedSince;

    private readonly CopyFence _fence = new();
    private bool _fenceUnsupported;
    private int _longCopies;
    private int _lateCopies;
    private TimeSpan _longestCopy;
    private long _lastCopyNote;

    /// <summary>Takes the control's frames over once the WebView has started (on the UI thread).</summary>
    public void Attach(WebView2CompositionControl control)
    {
        _control = control;
        if (PoolField is null || FrameArrived is null || RequestRender is null)
        {
            logger.LogWarning("The WebView's frames cannot be held with this WebView2 version (members not found)");
            return;
        }

        if (DeviceField is null || ContextField is null)
        {
            _fenceUnsupported = true;
            logger.LogWarning("The WebView's copies cannot be waited for with this WebView2 version (members not found)");
        }

        _hold = new DispatcherTimer(DispatcherPriority.Render, control.Dispatcher) { Interval = HoldStep };
        _hold.Tick += (_, _) => DeliverHeld();
        TakeOverPool();
        var check = new DispatcherTimer(DispatcherPriority.Background, control.Dispatcher) { Interval = PoolCheck };
        check.Tick += (_, _) => TakeOverPool();
        check.Start();
    }

    /// <summary>
    /// Takes the frames of the control's current pool over: its own handler off (the delegate compares equal to the one it
    /// added, the control removes it the same way), this one on. The image and pool exist only after the control has
    /// loaded; a pool replaced by a rebuild is taken over at the next check.
    /// </summary>
    private void TakeOverPool()
    {
        if (_control is null || ImageField?.GetValue(_control) is not { } image
            || PoolField?.GetValue(image) is not Direct3D11CaptureFramePool pool || ReferenceEquals(pool, _pool))
        {
            return;
        }

        try
        {
            var controls = (TypedEventHandler<Direct3D11CaptureFramePool, object>)Delegate.CreateDelegate(
                typeof(TypedEventHandler<Direct3D11CaptureFramePool, object>), image, FrameArrived!);
            pool.FrameArrived -= controls;
            pool.FrameArrived += OnFrameArrived;
            _pool = pool;
            logger.LogDebug("Frames of the WebView's capture taken over");
        }
        catch (Exception ex) when (ex is ObjectDisposedException or COMException)
        {
            logger.LogDebug("Taking over the WebView's frame pool failed: {Message}", ex.Message); // disposed by a rebuild: the next check
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        NoteArrived();
        if (!ReferenceEquals(pool, _pool) || _hold is not { } hold || hold.IsEnabled)
        {
            // An old pool; or a frame already waits. The pool has one buffer, so nothing new comes while it is held: what
            // changed meanwhile came as the next frame once the buffer was free (FerretSharp, observed under load).
            return;
        }

        if (CanWrite())
        {
            Render();
            return;
        }

        _heldSince = Stopwatch.GetTimestamp();
        hold.Start();
    }

    private void DeliverHeld()
    {
        var waited = Stopwatch.GetElapsedTime(_heldSince);
        var free = CanWrite();
        if (!free && waited < HoldLimit)
        {
            return;
        }

        _hold!.Stop();
        Render(); // past the limit (the render thread does not present): the control drops it as it would have
        NoteHeld(waited, dropped: !free);
    }

    /// <summary>
    /// Whether the control's copy will get the lock without waiting. The lock is free once WPF's render thread has presented
    /// the last copy; only a commit on this (the UI) thread takes it again, so it stays free until the copy below.
    /// </summary>
    private bool CanWrite()
    {
        if (_control is null || ImageField?.GetValue(_control) is not D3DImage image)
        {
            return true;
        }

        var free = image.TryLock(new Duration(TimeSpan.Zero));
        image.Unlock(); // TryLock counts the lock also when it failed
        return free;
    }

    private void Render()
    {
        if (_control is null || ImageField?.GetValue(_control) is not { } image)
        {
            return;
        }

        try
        {
            RequestRender!.Invoke(image, null);
        }
        catch (TargetInvocationException ex)
        {
            logger.LogDebug("Copying a WebView frame failed: {Message}", ex.InnerException?.Message); // device lost: the control rebuilds
            return;
        }

        WaitForCopy(image);
    }

    /// <summary>
    /// The copy runs on the GPU and WPF presents the texture from its own device, without waiting for it: waited for here,
    /// before this thread commits the frame to WPF's render thread. Also when nothing was copied – then the queue is empty
    /// and the wait ends at once.
    /// </summary>
    private void WaitForCopy(object image)
    {
        if (_fenceUnsupported || DeviceField!.GetValue(image) is not { } device || ContextField!.GetValue(image) is not { } context)
        {
            return;
        }

        try
        {
            NoteCopy(_fence.Wait(device, context, CopyLimit));
        }
        catch (InvalidCastException ex)
        {
            _fenceUnsupported = true;
            logger.LogWarning(ex, "The WebView's copies cannot be waited for (Direct3D interfaces not found)");
        }
        catch (COMException ex)
        {
            _fence.Release(); // device lost: the rebuild brings new devices, the next frame a new query
            logger.LogDebug("Waiting for the copy of a WebView frame failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// All frames of the capture, noted with the first one a minute after the last note: the span is the time since then,
    /// so a quiet page shows as few frames over a long span. On some machines (Windows 11 23H2) the capture delivers one
    /// for every frame shown, also without a change of the page – up to the display's refresh rate (FerretSharp ADR 0043).
    /// </summary>
    private void NoteArrived()
    {
        if (_arrivedSince == 0)
        {
            _arrivedSince = Stopwatch.GetTimestamp(); // starts the span and is not counted, as the frame that ends one
            return;
        }

        _arrived++;
        var span = Stopwatch.GetElapsedTime(_arrivedSince);
        if (span < NoteEvery)
        {
            return;
        }

        logger.LogDebug("WebView frames arrived: {Count} in {Seconds} s, {PerSecond:0.0} per second",
            _arrived, (int)span.TotalSeconds, _arrived / span.TotalSeconds);
        _arrived = 0;
        _arrivedSince = Stopwatch.GetTimestamp();
    }

    /// <summary>Copies that kept the UI thread waiting, at most once a minute in the log – and how many were still not done.</summary>
    private void NoteCopy(TimeSpan? waited)
    {
        if (waited is null)
        {
            _lateCopies++;
        }
        else if (waited >= CopyNoted)
        {
            _longCopies++;
            if (waited > _longestCopy)
            {
                _longestCopy = waited.Value;
            }
        }
        else
        {
            return;
        }

        if (_lastCopyNote != 0 && Stopwatch.GetElapsedTime(_lastCopyNote) < NoteEvery)
        {
            return;
        }

        logger.LogDebug("WebView copies waited for on the GPU: {Count} over {Noted} ms, longest {Longest} ms; not done after {Limit} ms: {Late}",
            _longCopies, (int)CopyNoted.TotalMilliseconds, (int)_longestCopy.TotalMilliseconds, (int)CopyLimit.TotalMilliseconds, _lateCopies);
        _longCopies = 0;
        _lateCopies = 0;
        _longestCopy = TimeSpan.Zero;
        _lastCopyNote = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// A held frame is normal under load; the log gets how often and how long, at most once a minute – and how many were
    /// still dropped after the limit (those are lost as before).
    /// </summary>
    private void NoteHeld(TimeSpan waited, bool dropped)
    {
        if (dropped)
        {
            _dropped++;
        }
        else
        {
            _held++;
        }

        if (waited > _longestHold)
        {
            _longestHold = waited;
        }

        if (_lastNote != 0 && Stopwatch.GetElapsedTime(_lastNote) < NoteEvery)
        {
            return;
        }

        logger.LogDebug("WebView frames held until the image was free: {Count}, longest {Longest} ms; dropped after {Limit} s: {Dropped}",
            _held, (int)_longestHold.TotalMilliseconds, HoldLimit.TotalSeconds, _dropped);
        _held = 0;
        _dropped = 0;
        _longestHold = TimeSpan.Zero;
        _lastNote = Stopwatch.GetTimestamp();
    }
}
