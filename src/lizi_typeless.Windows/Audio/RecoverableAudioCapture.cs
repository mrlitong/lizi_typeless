using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;
using lizi_typeless.Core.Sessions;

namespace lizi_typeless.Windows.Audio;

internal sealed class RecoverableAudioCapture : IDisposable
{
    private static readonly TimeSpan DurableFlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _sync = new();
    private readonly Stopwatch _flushClock = new();
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private FileStream? _rawStream;
    private WaveFileWriter? _waveWriter;
    private TaskCompletionSource<StoppedEventArgs>? _stopped;
    private bool _receivedFirstFrame;
    private bool _disposed;

    public event EventHandler? FirstFrameReceived;

    public event EventHandler<CapturedAudioEventArgs>? AudioAvailable;

    public AudioFormatInfo Start(string rawPath, string wavePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capture is not null)
        {
            throw new InvalidOperationException("Audio capture is already active.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        _deviceEnumerator = new MMDeviceEnumerator();
        _device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: 20);
        var format = ToFormatInfo(_capture.WaveFormat);

        _rawStream = new FileStream(
            rawPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough);
        _waveWriter = new WaveFileWriter(wavePath, _capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _stopped = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushClock.Start();
        _capture.StartRecording();
        return format;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var capture = _capture;
        var stopped = _stopped;
        if (capture is null || stopped is null)
        {
            return;
        }

        capture.StopRecording();
        var result = await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        FinalizeFiles();
        if (result.Exception is not null)
        {
            throw new InvalidOperationException("The Windows audio capture stopped with an error.", result.Exception);
        }
    }

    public static void CreateWaveFromRaw(
        string rawPath,
        string wavePath,
        AudioFormatInfo format)
    {
        var waveFormat = ToWaveFormat(format);
        using var input = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var writer = new WaveFileWriter(wavePath, waveFormat);
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            writer.Write(buffer, 0, count);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_capture is not null)
        {
            try
            {
                _capture.StopRecording();
            }
            catch (InvalidOperationException)
            {
                // The capture can already be stopped while normal cleanup runs.
            }
        }

        FinalizeFiles();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var notifyFirstFrame = false;
        byte[]? capturedAudio = null;
        lock (_sync)
        {
            if (_rawStream is null || _waveWriter is null)
            {
                return;
            }

            _rawStream.Write(args.Buffer, 0, args.BytesRecorded);
            _waveWriter.Write(args.Buffer, 0, args.BytesRecorded);

            if (_flushClock.Elapsed >= DurableFlushInterval)
            {
                _rawStream.Flush(flushToDisk: true);
                _waveWriter.Flush();
                _flushClock.Restart();
            }

            if (!_receivedFirstFrame && args.BytesRecorded > 0)
            {
                _receivedFirstFrame = true;
                notifyFirstFrame = true;
            }

            if (AudioAvailable is not null && args.BytesRecorded > 0)
            {
                capturedAudio = args.Buffer.AsSpan(0, args.BytesRecorded).ToArray();
            }
        }

        if (notifyFirstFrame)
        {
            FirstFrameReceived?.Invoke(this, EventArgs.Empty);
        }


        if (capturedAudio is not null)
        {
            AudioAvailable?.Invoke(this, new CapturedAudioEventArgs(capturedAudio));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args) => _stopped?.TrySetResult(args);

    private void FinalizeFiles()
    {
        lock (_sync)
        {
            _capture?.Dispose();
            _capture = null;
            _waveWriter?.Dispose();
            _waveWriter = null;
            if (_rawStream is not null)
            {
                _rawStream.Flush(flushToDisk: true);
                _rawStream.Dispose();
                _rawStream = null;
            }

            _device?.Dispose();
            _device = null;
            _deviceEnumerator?.Dispose();
            _deviceEnumerator = null;
        }
    }

    private static AudioFormatInfo ToFormatInfo(WaveFormat format)
    {
        var encoding = format.Encoding switch
        {
            WaveFormatEncoding.Pcm => "pcm",
            WaveFormatEncoding.IeeeFloat => "ieee-float",
            WaveFormatEncoding.Extensible when format is WaveFormatExtensible extensible &&
                extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM => "pcm",
            WaveFormatEncoding.Extensible when format is WaveFormatExtensible extensible &&
                extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT => "ieee-float",
            _ => throw new NotSupportedException($"Unsupported capture format: {format}.")
        };

        return new AudioFormatInfo(
            format.SampleRate,
            format.Channels,
            format.BitsPerSample,
            encoding,
            format.BlockAlign,
            format.AverageBytesPerSecond);
    }

    private static WaveFormat ToWaveFormat(AudioFormatInfo format) => format.Encoding switch
    {
        "pcm" => new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels),
        "ieee-float" when format.BitsPerSample == 32 =>
            WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels),
        _ => throw new NotSupportedException(
            $"Unsupported stored audio format: {format.Encoding}, {format.BitsPerSample} bits."),
    };
}

internal sealed record CapturedAudioEventArgs(byte[] Data);
