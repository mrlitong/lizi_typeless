using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using lizi_typeless.Windows.ViewModels;

namespace lizi_typeless.Windows;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private bool _closeForExit;

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (_closeForExit)
            {
                return;
            }

            args.Cancel = true;
            Hide();
        };
        _controller.SessionsChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        };
    }

    public async Task ShowAndRefreshAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    public void SetServiceStatus(string status) => ServiceStatusText.Text = $"推理服务：{status}";

    public void CloseForExit()
    {
        _closeForExit = true;
        Close();
    }

    private async Task RefreshAsync(string? selectSessionId = null)
    {
        var selectedId = selectSessionId ?? SelectedItem?.Session.Id;
        var sessions = await _controller.GetSessionsAsync().ConfigureAwait(true);
        var items = sessions.Select(session => new SessionListItem(session)).ToArray();
        SessionsList.ItemsSource = items;
        SessionsList.SelectedItem = items.FirstOrDefault(item => item.Session.Id == selectedId)
            ?? items.FirstOrDefault();
        ShowSelectedDetails();
    }

    private SessionListItem? SelectedItem => SessionsList.SelectedItem as SessionListItem;

    private void ShowSelectedDetails()
    {
        var item = SelectedItem;
        if (item is null)
        {
            DetailTitle.Text = "选择一条记录";
            DetailMeta.Text = string.Empty;
            ErrorText.Text = string.Empty;
            RawTranscriptText.Text = string.Empty;
            FinalText.Text = string.Empty;
            return;
        }

        var session = item.Session;
        DetailTitle.Text = item.StatusText;
        var duration = session.StoppedAt is null
            ? string.Empty
            : $" · {(session.StoppedAt.Value - session.StartedAt).TotalSeconds:F1} 秒";
        DetailMeta.Text = $"{session.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}{duration} · {session.TargetWindow.Title}";
        ErrorText.Text = session.Error;
        RawTranscriptText.Text = session.RawTranscript;
        FinalText.Text = session.FinalText;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs args)
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    private void SessionsList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        ShowSelectedDetails();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs args)
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            await _controller.RetryAsync(item.Session.Id).ConfigureAwait(true);
            await RefreshAsync(item.Session.Id).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Retry 失败：{exception.Message}\n\n原始录音和已有结果没有被删除。",
                "lizi_typeless",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs args)
    {
        var session = SelectedItem?.Session;
        if (session is null)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(session.FinalText)
            ? session.RawTranscript
            : session.FinalText;
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs args)
    {
        var session = SelectedItem?.Session;
        if (session is null)
        {
            return;
        }

        var path = _controller.GetAudioPath(session);
        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show(this, "没有找到这条记录的 WAV 文件。", "lizi_typeless");
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs args)
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            this,
            "确定删除这条记录及其原始录音吗？此操作无法撤销。",
            "删除记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _controller.DeleteSessionAsync(item.Session.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
