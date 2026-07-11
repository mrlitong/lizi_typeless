namespace lizi_typeless.Core.Hotkeys;

public enum CaptureState
{
    Idle,
    Recording,
    Processing,
}

public enum HotkeyCommand
{
    None,
    StartRecording,
    StopRecording,
}

public sealed class RightAltStateMachine
{
    private readonly TimeSpan _doubleTapWindow;
    private readonly TimeSpan _stopSuppressionWindow;
    private DateTimeOffset? _firstIdlePress;
    private DateTimeOffset _suppressStartUntil = DateTimeOffset.MinValue;
    private bool _rightAltIsDown;

    public RightAltStateMachine(
        TimeSpan? doubleTapWindow = null,
        TimeSpan? stopSuppressionWindow = null)
    {
        _doubleTapWindow = doubleTapWindow ?? TimeSpan.FromMilliseconds(350);
        _stopSuppressionWindow = stopSuppressionWindow ?? TimeSpan.FromMilliseconds(650);

        if (_doubleTapWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(doubleTapWindow));
        }

        if (_stopSuppressionWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopSuppressionWindow));
        }
    }

    public CaptureState State { get; private set; } = CaptureState.Idle;

    public HotkeyCommand OnKeyDown(DateTimeOffset timestamp)
    {
        if (_rightAltIsDown)
        {
            return HotkeyCommand.None;
        }

        _rightAltIsDown = true;

        if (State == CaptureState.Recording)
        {
            State = CaptureState.Processing;
            _firstIdlePress = null;
            _suppressStartUntil = timestamp + _stopSuppressionWindow;
            return HotkeyCommand.StopRecording;
        }

        if (State == CaptureState.Processing || timestamp < _suppressStartUntil)
        {
            return HotkeyCommand.None;
        }

        if (_firstIdlePress is { } firstPress &&
            timestamp >= firstPress &&
            timestamp - firstPress <= _doubleTapWindow)
        {
            _firstIdlePress = null;
            State = CaptureState.Recording;
            return HotkeyCommand.StartRecording;
        }

        _firstIdlePress = timestamp;
        return HotkeyCommand.None;
    }

    public void OnKeyUp() => _rightAltIsDown = false;

    public void MarkProcessingFinished()
    {
        if (State != CaptureState.Processing)
        {
            throw new InvalidOperationException($"Cannot finish processing while in {State}.");
        }

        State = CaptureState.Idle;
        _firstIdlePress = null;
    }

    public void MarkRecordingFailed(DateTimeOffset timestamp)
    {
        if (State != CaptureState.Recording)
        {
            throw new InvalidOperationException($"Cannot fail recording while in {State}.");
        }

        State = CaptureState.Idle;
        _firstIdlePress = null;
        _suppressStartUntil = timestamp + _stopSuppressionWindow;
    }
}
