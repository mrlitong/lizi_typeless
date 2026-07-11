using System.Threading;
using System.Windows;
using lizi_typeless.Windows.Infrastructure;

namespace lizi_typeless.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;
    private OverlayWindow? _overlay;
    private MainWindow? _historyWindow;
    private TrayIconHost? _trayIcon;
    private int _isExiting;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _singleInstance = new Mutex(initiallyOwned: true, "Local\\lizi_typeless", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("lizi_typeless 已经在运行。", "lizi_typeless");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, eventArgs) =>
        {
            DiagnosticLog.Write("Unhandled UI exception.", eventArgs.Exception);
            eventArgs.Handled = true;
            System.Windows.MessageBox.Show(
                $"发生未处理错误：{eventArgs.Exception.Message}\n\n已有录音会保留在本地。",
                "lizi_typeless",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        try
        {
            _overlay = new OverlayWindow();
            _controller = new AppController(_overlay);
            _historyWindow = new MainWindow(_controller);
            _trayIcon = new TrayIconHost(
                () => _ = _historyWindow.ShowAndRefreshAsync(),
                () => _ = ExitAsync());
            _controller.ServiceStatusChanged += (_, status) =>
            {
                _trayIcon.SetServiceStatus(status);
                _historyWindow.SetServiceStatus(status);
            };

            await _controller.InitializeAsync().ConfigureAwait(true);
            _trayIcon.ShowBalloon("lizi_typeless 已启动", "双击右 Alt 开始录音，再按一次结束。", System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Application startup failed.", exception);
            System.Windows.MessageBox.Show(
                $"lizi_typeless 启动失败：{exception.Message}",
                "lizi_typeless",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ExitAsync().ConfigureAwait(true);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstance.Dispose();
        }

        base.OnExit(args);
    }

    private async Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _isExiting, 1) != 0)
        {
            return;
        }

        if (_controller is not null)
        {
            await _controller.DisposeAsync().ConfigureAwait(true);
        }

        _trayIcon?.Dispose();
        _historyWindow?.CloseForExit();
        _overlay?.Close();
        Shutdown();
    }
}
