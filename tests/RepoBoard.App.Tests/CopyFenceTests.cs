using System.Runtime.InteropServices;
using RepoBoard.App.Services;

namespace RepoBoard.App.Tests;

/// <summary>
/// Against a real Direct3D 11 device (WARP, the software rasterizer – no GPU needed): the method slots of CopyFence's own
/// COM interfaces must match the real vtables, a wrong slot calls another method or crashes.
/// </summary>
public sealed class CopyFenceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The query is made once per context and used again; a rebuild of the control brings new devices, whose context gets a
    /// new query (one of the old device must not be used on them); after a release (device lost) the next wait makes one.
    /// </summary>
    [Fact]
    public void Waits_for_the_queued_work_of_each_context()
    {
        using var before = WarpDevice.Create();
        using var after = WarpDevice.Create();
        var fence = new CopyFence();

        Assert.NotNull(fence.Wait(before.Device, before.Context, Limit));
        Assert.NotNull(fence.Wait(before.Device, before.Context, Limit));
        Assert.NotNull(fence.Wait(after.Device, after.Context, Limit));
        fence.Release();
        Assert.NotNull(fence.Wait(after.Device, after.Context, Limit));
        fence.Release();
    }

    private sealed class WarpDevice : IDisposable
    {
        private const int DriverTypeWarp = 5;
        private const uint SdkVersion = 7;

        private WarpDevice(object device, object context)
        {
            Device = device;
            Context = context;
        }

        public object Device { get; }

        public object Context { get; }

        public static WarpDevice Create()
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, DriverTypeWarp, IntPtr.Zero, 0, IntPtr.Zero, 0, SdkVersion,
                out var device, out _, out var context));
            try
            {
                return new WarpDevice(Marshal.GetObjectForIUnknown(device), Marshal.GetObjectForIUnknown(context));
            }
            finally
            {
                Marshal.Release(context); // the RCWs hold their own references
                Marshal.Release(device);
            }
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(Context);
            Marshal.ReleaseComObject(Device);
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
            uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);
    }
}
