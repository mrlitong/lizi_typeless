using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace lizi_typeless.Windows;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    private readonly DispatcherTimer _timer;
    private DateTimeOffset _recordingStartedAt;

    public OverlayWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            OnTick,
            Dispatcher);
        _timer.Stop();
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowRecording(DateTimeOffset startedAt)
    {
        _recordingStartedAt = startedAt;
        StatusText.Text = "正在录音";
        StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 91, 58));
        PreviewText.Text = "请开始说话…";
        DurationText.Text = "00:00";
        _timer.Start();
        ShowPassive();
    }

    public void UpdatePreview(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            PreviewText.Text = text;
        }
    }

    public void ShowProcessing(string stage)
    {
        _timer.Stop();
        StatusText.Text = stage;
        StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 177, 52));
        DurationText.Text = string.Empty;
        ShowPassive();
    }

    public void ShowResult(string status, string text, bool success)
    {
        _timer.Stop();
        StatusText.Text = status;
        StatusDot.Fill = new SolidColorBrush(
            success
                ? System.Windows.Media.Color.FromRgb(61, 184, 139)
                : System.Windows.Media.Color.FromRgb(244, 91, 58));
        DurationText.Text = string.Empty;
        PreviewText.Text = text;
        ShowPassive();
    }

    private void ShowPassive()
    {
        if (!IsVisible)
        {
            Show();
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - ActualWidth) / 2);
        Top = workArea.Bottom - ActualHeight - 36;
    }

    private void OnTick(object? sender, EventArgs args)
    {
        var elapsed = DateTimeOffset.Now - _recordingStartedAt;
        DurationText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExstyle, new nint(style | WsExNoactivate | WsExToolwindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
