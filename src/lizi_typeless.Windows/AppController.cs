using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using lizi_typeless.Core.Hotkeys;
using lizi_typeless.Core.Sessions;
using lizi_typeless.Windows.Audio;
using lizi_typeless.Windows.Inference;
using lizi_typeless.Windows.Infrastructure;
using lizi_typeless.Windows.Input;

namespace lizi_typeless.Windows;

internal sealed class AppController : IAsyncDisposable
{
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(1500);

    private readonly OverlayWindow _overlay;
    private readonly SessionStore _store = new(AppPaths.SessionsDirectory);
    private readonly RightAltStateMachine _hotkeyState = new();
    private readonly InferenceClient _inferenceClient = new(new Uri("http://127.0.0.1:8765/"));
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly InferenceServiceManager _serviceManager;
    private readonly ConcurrentQueue<byte[]> _pendingStreamingAudio = new();

    private GlobalKeyboardHook? _keyboardHook;
    private RecoverableAudioCapture? _audioCapture;
    private StreamingInferenceSession? _streamingInference;
    private SessionRecord? _currentSession;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _overlayHideCancellation;
    private Stopwatch? _captureStartClock;
    private double? _captureStartMilliseconds;
    private double? _firstAudioFrameMilliseconds;
    private bool _disposed;
    private bool _streamingReady;

    public AppController(OverlayWindow overlay)
    {
        _overlay = overlay;
        _serviceManager = new InferenceServiceManager(_inferenceClient);
    }

    public event EventHandler? SessionsChanged;

    public event EventHandler<string>? ServiceStatusChanged;

    public async Task InitializeAsync()
    {
        await RecoverInterruptedSessionsAsync().ConfigureAwait(true);
        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.RightAltChanged += OnRightAltChanged;
        _ = WarmInferenceServiceAsync();
    }

    public Task<IReadOnlyList<SessionRecord>> GetSessionsAsync(
        CancellationToken cancellationToken = default) => _store.LoadAllAsync(cancellationToken);

    public string GetAudioPath(SessionRecord session) => _store.ResolveFile(session, session.AudioFile);

    public async Task RetryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        SessionRecord? session = null;
        try
        {
            session = await _store.LoadAsync(sessionId, cancellationToken).ConfigureAwait(true);
            var retry = SessionTransitions.PlanRetry(session);
            session = SessionTransitions.MoveTo(session, SessionStatus.Processing, DateTimeOffset.Now) with
            {
                FailureStage = FailureStage.None,
                Error = string.Empty,
                RetryCount = session.RetryCount + 1,
            };
            await _store.SaveAsync(session, cancellationToken).ConfigureAwait(true);
            RaiseSessionsChanged();

            PublishServiceStatus("正在启动");
            var health = await _serviceManager.EnsureReadyAsync(cancellationToken).ConfigureAwait(true);
            _streamingReady = health.Streaming;
            PublishServiceStatus($"已就绪 · {health.AsrModel}");

            if (retry.Step == RetryStep.Transcribe)
            {
                var audioPath = GetAudioPath(session);
                var transcription = await _inferenceClient.TranscribeAsync(
                        audioPath,
                        preview: false,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(transcription.Text))
                {
                    throw new InvalidDataException("No speech was detected in the saved audio.");
                }

                session = session with
                {
                    RawTranscript = transcription.Text.Trim(),
                    Language = transcription.Language,
                    Timings = session.Timings with
                    {
                        TranscriptionMilliseconds = transcription.DurationMilliseconds,
                    },
                    UpdatedAt = DateTimeOffset.Now,
                };
                await _store.SaveAsync(session, cancellationToken).ConfigureAwait(true);
            }

            var organization = await _inferenceClient.OrganizeAsync(
                    session.RawTranscript,
                    cancellationToken)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(organization.Text))
            {
                throw new InvalidDataException("The organizer returned empty text.");
            }

            session = SessionTransitions.MoveTo(session, SessionStatus.Ready, DateTimeOffset.Now) with
            {
                FinalText = organization.Text.Trim(),
                Timings = session.Timings with
                {
                    OrganizationMilliseconds = organization.DurationMilliseconds,
                },
                FailureStage = FailureStage.None,
                Error = string.Empty,
                WasAutomaticallyInserted = false,
            };
            await _store.SaveAsync(session, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DiagnosticLog.Write($"Retry failed for session {sessionId}.", exception);
            if (session is not null && SessionTransitions.CanMove(session.Status, SessionStatus.Failed))
            {
                session = SessionTransitions.MoveTo(session, SessionStatus.Failed, DateTimeOffset.Now) with
                {
                    FailureStage = string.IsNullOrWhiteSpace(session.RawTranscript)
                        ? FailureStage.Transcription
                        : FailureStage.Organization,
                    Error = "Retry 失败，原始录音和已有文字仍已保留。",
                };
                await _store.SaveAsync(session, CancellationToken.None).ConfigureAwait(true);
            }

            throw;
        }
        finally
        {
            RaiseSessionsChanged();
            _operationLock.Release();
        }
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_currentSession?.Id == sessionId)
            {
                throw new InvalidOperationException("The active recording cannot be deleted.");
            }

            await _store.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(true);
            RaiseSessionsChanged();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _shutdown.Cancel();
        _previewCancellation?.Cancel();
        _overlayHideCancellation?.Cancel();

        await _operationLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_audioCapture is not null)
            {
                try
                {
                    await _audioCapture.StopAsync().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    DiagnosticLog.Write("Audio capture cleanup failed during exit.", exception);
                }

                _audioCapture.Dispose();
                _audioCapture = null;
            }

            if (_streamingInference is not null)
            {
                await _streamingInference.DisposeAsync().ConfigureAwait(true);
                _streamingInference = null;
            }

            if (_currentSession is not null &&
                SessionTransitions.CanMove(_currentSession.Status, SessionStatus.Failed))
            {
                _currentSession = SessionTransitions.MoveTo(
                    _currentSession,
                    SessionStatus.Failed,
                    DateTimeOffset.Now) with
                {
                    FailureStage = FailureStage.Recording,
                    Error = "应用退出，已经录制的音频已保存，可从历史记录 Retry。",
                };
                await _store.SaveAsync(_currentSession, CancellationToken.None).ConfigureAwait(true);
            }
        }
        finally
        {
            _operationLock.Release();
        }

        await _serviceManager.DisposeAsync().ConfigureAwait(true);
        _inferenceClient.Dispose();
        _previewCancellation?.Dispose();
        _overlayHideCancellation?.Dispose();
        _shutdown.Dispose();
        _operationLock.Dispose();
    }

    private void OnRightAltChanged(object? sender, RightAltEventArgs args)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!args.IsKeyDown)
            {
                _hotkeyState.OnKeyUp();
                return;
            }

            var command = _hotkeyState.OnKeyDown(args.Timestamp);
            if (command == HotkeyCommand.StartRecording)
            {
                _ = StartRecordingAsync(args.Timestamp);
            }
            else if (command == HotkeyCommand.StopRecording)
            {
                _ = StopRecordingAsync();
            }
        });
    }

    private async Task StartRecordingAsync(DateTimeOffset timestamp)
    {
        var captureStartClock = Stopwatch.StartNew();
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(true))
        {
            _hotkeyState.MarkRecordingFailed(timestamp);
            return;
        }

        SessionRecord? session = null;
        try
        {
            var target = TargetWindowService.Capture();
            session = await _store.CreateAsync(target, timestamp, _shutdown.Token).ConfigureAwait(true);
            _currentSession = session;

            var rawPath = _store.ResolveFile(session, session.RawAudioFile);
            var wavePath = _store.ResolveFile(session, session.AudioFile);
            _captureStartClock = captureStartClock;
            _captureStartMilliseconds = null;
            _firstAudioFrameMilliseconds = null;
            _audioCapture = new RecoverableAudioCapture();
            _audioCapture.FirstFrameReceived += OnFirstAudioFrame;
            if (_streamingReady)
            {
                _audioCapture.AudioAvailable += OnAudioAvailable;
            }
            var format = _audioCapture.Start(rawPath, wavePath);
            if (_streamingReady)
            {
                _streamingInference = new StreamingInferenceSession(
                    new Uri("ws://127.0.0.1:8765/v1/stream"),
                    format);
                _streamingInference.PreviewReceived += OnStreamingPreview;
                while (_pendingStreamingAudio.TryDequeue(out var bufferedAudio))
                {
                    _streamingInference.TryAppend(bufferedAudio);
                }
            }
            session = session with
            {
                AudioFormat = format,
                UpdatedAt = DateTimeOffset.Now,
            };
            _currentSession = session;
            await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);

            CancelScheduledOverlayHide();
            _overlay.ShowRecording(timestamp);
            _captureStartMilliseconds = captureStartClock.Elapsed.TotalMilliseconds;
            if (_streamingInference is null)
            {
                _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                _ = RunPreviewLoopAsync(session.Id, _previewCancellation.Token);
            }
            RaiseSessionsChanged();
            DiagnosticLog.Write(
                $"Recording {session.Id} started: {format.SampleRate} Hz, {format.Channels} channel(s), " +
                $"{format.BitsPerSample}-bit {format.Encoding}; recording ready after " +
                $"{_captureStartMilliseconds:F1} ms.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DiagnosticLog.Write("Recording failed to start.", exception);
            _audioCapture?.Dispose();
            _audioCapture = null;
            if (_streamingInference is not null)
            {
                await _streamingInference.DisposeAsync().ConfigureAwait(true);
                _streamingInference = null;
            }
            ClearPendingStreamingAudio();
            if (session is not null)
            {
                session = SessionTransitions.MoveTo(session, SessionStatus.Failed, DateTimeOffset.Now) with
                {
                    FailureStage = FailureStage.Recording,
                    Error = "无法开始录音，请检查麦克风权限和默认输入设备。",
                };
                await _store.SaveAsync(session, CancellationToken.None).ConfigureAwait(true);
            }

            _currentSession = null;
            _captureStartClock = null;
            _captureStartMilliseconds = null;
            _firstAudioFrameMilliseconds = null;
            _hotkeyState.MarkRecordingFailed(DateTimeOffset.Now);
            _overlay.ShowResult("录音失败", "请检查麦克风权限和默认输入设备", success: false);
            ScheduleOverlayHide(TimeSpan.FromSeconds(4));
            RaiseSessionsChanged();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task StopRecordingAsync()
    {
        var endToResultClock = Stopwatch.StartNew();
        await _operationLock.WaitAsync().ConfigureAwait(true);
        var session = _currentSession;
        var failureStage = FailureStage.Recording;
        double? streamingFinishMilliseconds = null;
        try
        {
            if (session is null || _audioCapture is null)
            {
                throw new InvalidOperationException("No active recording exists.");
            }

            _previewCancellation?.Cancel();
            _overlay.ShowProcessing("正在保存录音");
            await _audioCapture.StopAsync(_shutdown.Token).ConfigureAwait(true);
            _audioCapture.AudioAvailable -= OnAudioAvailable;
            _audioCapture.Dispose();
            _audioCapture = null;

            session = SessionTransitions.MoveTo(session, SessionStatus.Processing, DateTimeOffset.Now) with
            {
                StoppedAt = DateTimeOffset.Now,
                AudioFormat = session.AudioFormat,
                Timings = session.Timings with
                {
                    CaptureStartMilliseconds = _captureStartMilliseconds,
                    FirstAudioFrameMilliseconds = _firstAudioFrameMilliseconds,
                },
            };
            _currentSession = session;
            await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);
            RaiseSessionsChanged();

            PublishServiceStatus("正在启动");
            _overlay.ShowProcessing("正在转录");
            failureStage = FailureStage.Transcription;
            var health = await _serviceManager.EnsureReadyAsync(_shutdown.Token).ConfigureAwait(true);
            _streamingReady = health.Streaming;
            PublishServiceStatus($"已就绪 · {health.AsrModel}");
            TranscriptionResult transcription;
            if (_streamingInference is not null)
            {
                try
                {
                    var streamed = await _streamingInference.FinishAsync(_shutdown.Token)
                        .ConfigureAwait(true);
                    transcription = new TranscriptionResult(
                        streamed.Text,
                        streamed.Language,
                        streamed.DurationMilliseconds);
                    streamingFinishMilliseconds = streamed.FinishMilliseconds;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    DiagnosticLog.Write("Streaming transcription failed; using the saved WAV.", exception);
                    transcription = await _inferenceClient.TranscribeAsync(
                            GetAudioPath(session),
                            preview: false,
                            _shutdown.Token)
                        .ConfigureAwait(true);
                }
                finally
                {
                    await _streamingInference.DisposeAsync().ConfigureAwait(true);
                    _streamingInference = null;
                }
            }
            else
            {
                transcription = await _inferenceClient.TranscribeAsync(
                        GetAudioPath(session),
                        preview: false,
                        _shutdown.Token)
                    .ConfigureAwait(true);
            }
            if (string.IsNullOrWhiteSpace(transcription.Text))
            {
                throw new InvalidDataException("No speech was detected.");
            }

            session = session with
            {
                RawTranscript = transcription.Text.Trim(),
                Language = transcription.Language,
                Timings = session.Timings with
                {
                    TranscriptionMilliseconds = transcription.DurationMilliseconds,
                    StreamingFinishMilliseconds = streamingFinishMilliseconds,
                    EndToTranscriptionMilliseconds = endToResultClock.Elapsed.TotalMilliseconds,
                },
                UpdatedAt = DateTimeOffset.Now,
            };
            _currentSession = session;
            await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);

            _overlay.ShowProcessing("正在整理");
            failureStage = FailureStage.Organization;
            var organization = await _inferenceClient.OrganizeAsync(
                    session.RawTranscript,
                    _shutdown.Token)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(organization.Text))
            {
                throw new InvalidDataException("The organizer returned empty text.");
            }

            session = SessionTransitions.MoveTo(session, SessionStatus.Ready, DateTimeOffset.Now) with
            {
                FinalText = organization.Text.Trim(),
                Timings = session.Timings with
                {
                    OrganizationMilliseconds = organization.DurationMilliseconds,
                    EndToOrganizationMilliseconds = endToResultClock.Elapsed.TotalMilliseconds,
                },
                FailureStage = FailureStage.None,
                Error = string.Empty,
            };
            _currentSession = session;
            await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);

            if (!TargetWindowService.IsStillForeground(session.TargetWindow))
            {
                session = session with
                {
                    FailureStage = FailureStage.TextInsertion,
                    Error = "目标窗口不可写或已变化，结果未自动写入。",
                    UpdatedAt = DateTimeOffset.Now,
                };
                await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);
                _overlay.ShowResult("目标窗口不可写或已变化", "结果已保存，请从历史记录复制", success: false);
                return;
            }

            failureStage = FailureStage.TextInsertion;
            var insertionClock = Stopwatch.StartNew();
            TextInserter.Insert(session.TargetWindow, session.FinalText);
            insertionClock.Stop();
            session = SessionTransitions.MoveTo(session, SessionStatus.Completed, DateTimeOffset.Now) with
            {
                InsertedAt = DateTimeOffset.Now,
                WasAutomaticallyInserted = true,
                Timings = session.Timings with
                {
                    TextInsertionMilliseconds = insertionClock.Elapsed.TotalMilliseconds,
                    EndToInsertionMilliseconds = endToResultClock.Elapsed.TotalMilliseconds,
                },
            };
            await _store.SaveAsync(session, _shutdown.Token).ConfigureAwait(true);
            _overlay.ShowResult("已输入", session.FinalText, success: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DiagnosticLog.Write($"Session {session?.Id ?? "unknown"} failed during {failureStage}.", exception);
            if (session is not null)
            {
                if (session.Status == SessionStatus.Ready && failureStage == FailureStage.TextInsertion)
                {
                    session = session with
                    {
                        FailureStage = failureStage,
                        Error = "文字写入失败，结果已保存，请从历史记录复制。",
                        UpdatedAt = DateTimeOffset.Now,
                    };
                }
                else if (SessionTransitions.CanMove(session.Status, SessionStatus.Failed))
                {
                    session = SessionTransitions.MoveTo(session, SessionStatus.Failed, DateTimeOffset.Now) with
                    {
                        FailureStage = failureStage,
                        Error = FailureMessage(failureStage),
                    };
                }

                await _store.SaveAsync(session, CancellationToken.None).ConfigureAwait(true);
            }

            _overlay.ShowResult("处理失败，录音已保存", FailureMessage(failureStage), success: false);
        }
        finally
        {
            _audioCapture?.Dispose();
            _audioCapture = null;
            if (_streamingInference is not null)
            {
                await _streamingInference.DisposeAsync().ConfigureAwait(true);
                _streamingInference = null;
            }
            ClearPendingStreamingAudio();
            _currentSession = null;
            _captureStartClock = null;
            _captureStartMilliseconds = null;
            _firstAudioFrameMilliseconds = null;
            _previewCancellation?.Dispose();
            _previewCancellation = null;
            if (_hotkeyState.State == CaptureState.Processing)
            {
                _hotkeyState.MarkProcessingFinished();
            }

            RaiseSessionsChanged();
            _operationLock.Release();
            ScheduleOverlayHide(TimeSpan.FromSeconds(3));
        }
    }

    private void OnFirstAudioFrame(object? sender, EventArgs args)
    {
        if (_captureStartClock is null || _firstAudioFrameMilliseconds is not null)
        {
            return;
        }

        _firstAudioFrameMilliseconds = _captureStartClock.Elapsed.TotalMilliseconds;
        DiagnosticLog.Write($"First audio frame arrived after {_firstAudioFrameMilliseconds:F1} ms.");
    }

    private void OnAudioAvailable(object? sender, CapturedAudioEventArgs args)
    {
        var streaming = _streamingInference;
        if (streaming is not null && streaming.TryAppend(args.Data))
        {
            return;
        }

        _pendingStreamingAudio.Enqueue(args.Data);
    }

    private void OnStreamingPreview(object? sender, string text)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(
            () => _overlay.UpdatePreview(text));
    }

    private async Task RunPreviewLoopAsync(string sessionId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? snapshot = null;
            try
            {
                await Task.Delay(PreviewInterval, cancellationToken).ConfigureAwait(true);
                var session = _currentSession;
                if (session?.Id != sessionId ||
                    session.Status != SessionStatus.Recording ||
                    session.AudioFormat is null)
                {
                    return;
                }

                var health = await _inferenceClient.GetHealthAsync(cancellationToken).ConfigureAwait(true);
                if (!health.Ready)
                {
                    continue;
                }

                var rawPath = _store.ResolveFile(session, session.RawAudioFile);
                if (!File.Exists(rawPath) || new FileInfo(rawPath).Length == 0)
                {
                    continue;
                }

                snapshot = Path.Combine(
                    Path.GetDirectoryName(rawPath)!,
                    $"preview-{Guid.NewGuid():N}.wav");
                RecoverableAudioCapture.CreateWaveFromRaw(rawPath, snapshot, session.AudioFormat);
                var preview = await _inferenceClient.TranscribeAsync(
                        snapshot,
                        preview: true,
                        cancellationToken)
                    .ConfigureAwait(true);
                _overlay.UpdatePreview(preview.Text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write("Live preview failed; recording continues.", exception);
            }
            finally
            {
                if (snapshot is not null && File.Exists(snapshot))
                {
                    File.Delete(snapshot);
                }
            }
        }
    }

    private async Task RecoverInterruptedSessionsAsync()
    {
        var sessions = await _store.LoadAllAsync().ConfigureAwait(true);
        foreach (var session in sessions.Where(
                     item => item.Status is SessionStatus.Recording or SessionStatus.Processing))
        {
            var recovered = session;
            try
            {
                var rawPath = _store.ResolveFile(session, session.RawAudioFile);
                var wavePath = _store.ResolveFile(session, session.AudioFile);
                if (File.Exists(rawPath) &&
                    new FileInfo(rawPath).Length > 0 &&
                    session.AudioFormat is not null)
                {
                    RecoverableAudioCapture.CreateWaveFromRaw(rawPath, wavePath, session.AudioFormat);
                }

                recovered = SessionTransitions.MoveTo(session, SessionStatus.Failed, DateTimeOffset.Now) with
                {
                    FailureStage = FailureStage.Recovery,
                    Error = "发现上次未完成的会话，录音已恢复，可从历史记录 Retry。",
                };
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Failed to recover session {session.Id}.", exception);
                recovered = SessionTransitions.MoveTo(session, SessionStatus.Failed, DateTimeOffset.Now) with
                {
                    FailureStage = FailureStage.Recovery,
                    Error = "发现上次未完成的会话；原始音频仍保留，但 WAV 恢复失败。",
                };
            }

            await _store.SaveAsync(recovered).ConfigureAwait(true);
        }
    }

    private async Task WarmInferenceServiceAsync()
    {
        try
        {
            PublishServiceStatus("正在启动");
            var health = await _serviceManager.EnsureReadyAsync(_shutdown.Token).ConfigureAwait(true);
            _streamingReady = health.Streaming;
            PublishServiceStatus($"已就绪 · {health.AsrModel}");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Inference service warm-up failed.", exception);
            PublishServiceStatus("不可用");
        }
    }

    private void PublishServiceStatus(string status)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ServiceStatusChanged?.Invoke(this, status);
            return;
        }

        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(
            () => ServiceStatusChanged?.Invoke(this, status));
    }

    private void RaiseSessionsChanged()
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleOverlayHide(TimeSpan delay)
    {
        CancelScheduledOverlayHide();
        _overlayHideCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _ = HideOverlayAfterAsync(delay, _overlayHideCancellation.Token);
    }

    private void CancelScheduledOverlayHide()
    {
        _overlayHideCancellation?.Cancel();
        _overlayHideCancellation?.Dispose();
        _overlayHideCancellation = null;
    }

    private async Task HideOverlayAfterAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            _overlay.Hide();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string FailureMessage(FailureStage stage) => stage switch
    {
        FailureStage.Recording => "录音保存失败，请检查麦克风和本地磁盘。",
        FailureStage.Transcription => "转录失败，原始录音已保存，可稍后 Retry。",
        FailureStage.Organization => "整理失败，原始录音和转录已保存，可稍后 Retry。",
        FailureStage.TextInsertion => "文字写入失败，结果已保存，请从历史记录复制。",
        _ => "处理失败，已有内容已保存。",
    };

    private void ClearPendingStreamingAudio()
    {
        while (_pendingStreamingAudio.TryDequeue(out _))
        {
        }
    }
}
