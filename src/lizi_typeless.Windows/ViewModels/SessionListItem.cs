using lizi_typeless.Core.Sessions;

namespace lizi_typeless.Windows.ViewModels;

internal sealed class SessionListItem
{
    public SessionListItem(SessionRecord session)
    {
        Session = session;
    }

    public SessionRecord Session { get; }

    public string DisplayTime => Session.StartedAt.LocalDateTime.ToString("MM-dd HH:mm:ss");

    public string StatusText => Session.Status switch
    {
        SessionStatus.Recording => "录音中",
        SessionStatus.Processing => "处理中",
        SessionStatus.Ready => "待复制",
        SessionStatus.Completed => "已输入",
        SessionStatus.Failed => "失败",
        _ => Session.Status.ToString(),
    };

    public string Summary
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Session.FinalText)
                ? Session.RawTranscript
                : Session.FinalText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.ReplaceLineEndings(" ");
            }

            return string.IsNullOrWhiteSpace(Session.Error) ? "原始录音已保存" : Session.Error;
        }
    }
}
