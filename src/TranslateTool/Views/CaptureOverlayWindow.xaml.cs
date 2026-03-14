using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using WinRT.Interop;

namespace TranslateTool.Views;

public sealed partial class CaptureOverlayWindow : Window
{
    private readonly TaskCompletionSource<Rect?> _tcs = new();
    private readonly int _screenLeft;
    private readonly int _screenTop;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    private bool _isDragging;
    private Point _dragStart;
    private Rect _currentRect;

    public CaptureOverlayWindow(byte[] previewBytes, int screenLeft, int screenTop, int screenWidth, int screenHeight)
    {
        InitializeComponent();

        _screenLeft = screenLeft;
        _screenTop = screenTop;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;

        Activated += OnActivated;
        Closed += OnClosed;
        _ = LoadPreviewAsync(previewBytes);
    }

    public Task<Rect?> CaptureRegionAsync()
    {
        Activate();
        return _tcs.Task;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        PositionAndSizeOverlay();
        _ = RootGrid.Focus(FocusState.Programmatic);
    }

    private void PositionAndSizeOverlay()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(_screenWidth, _screenHeight));
        appWindow.Move(new Windows.Graphics.PointInt32(_screenLeft, _screenTop));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }
    }

    private async Task LoadPreviewAsync(byte[] bytes)
    {
        var image = new BitmapImage();
        await using var ms = new MemoryStream(bytes);
        using var ras = ms.AsRandomAccessStream();
        await image.SetSourceAsync(ras);
        ScreenPreview.Source = image;
    }

    private void OnOverlayPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid).Position;
        _isDragging = true;
        _dragStart = point;
        _currentRect = new Rect(point.X, point.Y, 1, 1);
        RenderSelection();
    }

    private void OnOverlayMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(RootGrid).Position;
        var left = Math.Min(_dragStart.X, point.X);
        var top = Math.Min(_dragStart.Y, point.Y);
        var width = Math.Max(1, Math.Abs(point.X - _dragStart.X));
        var height = Math.Max(1, Math.Abs(point.Y - _dragStart.Y));
        _currentRect = new Rect(left, top, width, height);
        RenderSelection();
    }

    private void OnOverlayReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ConfirmButton.IsEnabled = _currentRect.Width >= 5 && _currentRect.Height >= 5;
    }

    private void RenderSelection()
    {
        SelectionRect.Visibility = Visibility.Visible;
        PixelBadge.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionRect, _currentRect.X);
        Canvas.SetTop(SelectionRect, _currentRect.Y);
        SelectionRect.Width = _currentRect.Width;
        SelectionRect.Height = _currentRect.Height;

        PixelBadgeText.Text = $"{(int)_currentRect.Width} x {(int)_currentRect.Height}";

        var badgeLeft = Math.Min(_currentRect.X + 8, Math.Max(0, _screenWidth - 120));
        var badgeTop = Math.Max(0, _currentRect.Y - 30);
        Canvas.SetLeft(PixelBadge, badgeLeft);
        Canvas.SetTop(PixelBadge, badgeTop);
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        var rect = new Rect(
            Math.Clamp(_currentRect.X, 0, _screenWidth - 1),
            Math.Clamp(_currentRect.Y, 0, _screenHeight - 1),
            Math.Clamp(_currentRect.Width, 1, _screenWidth),
            Math.Clamp(_currentRect.Height, 1, _screenHeight));

        _tcs.TrySetResult(rect);
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(null);
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _tcs.TrySetResult(null);
            Close();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _tcs.TrySetResult(null);
    }
}
