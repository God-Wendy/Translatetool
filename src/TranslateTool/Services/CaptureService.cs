using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TranslateTool.Models;
using TranslateTool.Views;
using WinRT.Interop;
using Rect = Windows.Foundation.Rect;

namespace TranslateTool.Services;

public sealed class CaptureService : ICaptureService
{
    public async Task<CapturedImage?> BeginAsync(CaptureOptions options, Window hostWindow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostWindow);

        var hostHandle = WindowNative.GetWindowHandle(hostWindow);
        if (options.HideCurrentPopup)
        {
            _ = ShowWindow(hostHandle, SwHide);
            await Task.Delay(150, cancellationToken);
        }

        try
        {
            var screenLeft = GetSystemMetrics(SmXVirtualScreen);
            var screenTop = GetSystemMetrics(SmYVirtualScreen);
            var screenWidth = GetSystemMetrics(SmCxVirtualScreen);
            var screenHeight = GetSystemMetrics(SmCyVirtualScreen);

            using var full = CaptureVirtualScreen(screenLeft, screenTop, screenWidth, screenHeight);
            var bytes = EncodeBitmap(full);

            var overlay = new CaptureOverlayWindow(bytes, screenLeft, screenTop, screenWidth, screenHeight);
            var selectedRect = await overlay.CaptureRegionAsync();
            if (selectedRect is null)
            {
                return null;
            }

            using var cropped = CropBitmap(full, selectedRect.Value);
            var outBytes = EncodeBitmap(cropped);
            return new CapturedImage(outBytes, selectedRect.Value);
        }
        finally
        {
            if (options.HideCurrentPopup)
            {
                _ = ShowWindow(hostHandle, SwShow);
                _ = SetForegroundWindow(hostHandle);
            }
        }
    }

    private static Bitmap CaptureVirtualScreen(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bmp);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static Bitmap CropBitmap(Bitmap source, Rect region)
    {
        var rect = new Rectangle(
            Math.Max(0, (int)Math.Floor(region.X)),
            Math.Max(0, (int)Math.Floor(region.Y)),
            Math.Max(1, (int)Math.Floor(region.Width)),
            Math.Max(1, (int)Math.Floor(region.Height)));

        rect.Width = Math.Min(rect.Width, source.Width - rect.X);
        rect.Height = Math.Min(rect.Height, source.Height - rect.Y);

        var outBmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(outBmp);
        graphics.DrawImage(source, new Rectangle(0, 0, outBmp.Width, outBmp.Height), rect, GraphicsUnit.Pixel);
        return outBmp;
    }

    private static byte[] EncodeBitmap(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private const int SwHide = 0;
    private const int SwShow = 5;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
