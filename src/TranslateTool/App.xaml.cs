using Microsoft.UI.Xaml.Navigation;
using TranslateTool.Services;
using TranslateTool.Views;
using System.Runtime.InteropServices;
using System.Text;

namespace TranslateTool;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public AppServices Services { get; }
    public Window MainWindow { get; private set; } = null!;

    public App()
    {
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            LogStartupError("InitializeComponent failed", ex);
            ShowNativeError("应用启动失败", ex.ToString());
            throw;
        }

        Services = new AppServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        try
        {
            MainWindow ??= new Window();

            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                MainWindow.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            LogStartupError("OnLaunched failed", ex);
            ShowNativeError("应用启动失败", ex.ToString());
            throw;
        }
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        LogStartupError($"Navigation failed: {e.SourcePageType.FullName}", e.Exception);
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    private static void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogStartupError("AppDomain unhandled exception", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogStartupError("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogStartupError("XAML unhandled exception", e.Exception);
        ShowNativeError("应用启动异常", e.Exception.ToString());
        e.Handled = true;
    }

    private static void LogStartupError(string title, Exception? ex)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslateTool");
            Directory.CreateDirectory(root);
            var logPath = Path.Combine(root, "startup.log");

            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}");
            if (ex is null)
            {
                builder.AppendLine("Exception: <null>");
            }
            else
            {
                builder.AppendLine(ex.ToString());
            }

            builder.AppendLine(new string('-', 100));
            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static void ShowNativeError(string title, string message)
    {
        try
        {
            const int MbOk = 0x00000000;
            _ = MessageBoxW(IntPtr.Zero, message, title, MbOk);
        }
        catch
        {
            // ignore UI error dialog failures
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, int type);
}
