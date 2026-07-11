using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using lizi_typeless.Core.Sessions;

namespace lizi_typeless.Windows.Inference;

internal sealed class StreamingInferenceSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Uri _endpoint;
    private readonly AudioFormatInfo _format;
    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<StreamingTranscriptionResult> _runTask;

    public StreamingInferenceSession(Uri endpoint, AudioFormatInfo format)
    {
        _endpoint = endpoint;
        _format = format;
        _runTask = RunAsync(_cancellation.Token);
    }

    public event EventHandler<string>? PreviewReceived;

    public bool TryAppend(byte[] audio) => _audio.Writer.TryWrite(audio);

    public async Task<StreamingTranscriptionResult> FinishAsync(
        CancellationToken cancellationToken = default)
    {
        _audio.Writer.TryComplete();
        return await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _audio.Writer.TryComplete();
        _cancellation.Cancel();
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (Exception) when (_cancellation.IsCancellationRequested)
        {
        }

        _cancellation.Dispose();
    }

    private async Task<StreamingTranscriptionResult> RunAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
        await SendJsonAsync(
                socket,
                new
                {
                    type = "start",
                    sampleRate = _format.SampleRate,
                    channels = _format.Channels,
                    bitsPerSample = _format.BitsPerSample,
                    encoding = _format.Encoding,
                    blockAlign = _format.BlockAlign,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var receiveTask = ReceiveAsync(socket, cancellationToken);
        await foreach (var chunk in _audio.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await socket.SendAsync(
                    chunk,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await SendJsonAsync(socket, new { type = "finish" }, cancellationToken).ConfigureAwait(false);
        return await receiveTask.ConfigureAwait(false);
    }

    private async Task<StreamingTranscriptionResult> ReceiveAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var json = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                break;
            }

            var message = JsonSerializer.Deserialize<StreamMessage>(json, JsonOptions)
                ?? throw new InvalidDataException("The streaming response was empty.");
            if (message.Type == "preview")
            {
                PreviewReceived?.Invoke(this, message.Text ?? string.Empty);
            }
            else if (message.Type == "final")
            {
                return new StreamingTranscriptionResult(
                    message.Text ?? string.Empty,
                    message.Language ?? string.Empty,
                    message.DurationMilliseconds,
                    message.FinishMilliseconds);
            }
            else if (message.Type == "error")
            {
                throw new InvalidOperationException(message.Message ?? "Streaming inference failed.");
            }
        }

        throw new WebSocketException("The streaming service closed before returning a final result.");
    }

    private static async Task SendJsonAsync(
        ClientWebSocket socket,
        object value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(
                json,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("The streaming service returned a non-text control message.");
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            }
        }
    }

    private sealed record StreamMessage(
        string Type,
        string? Text,
        string? Language,
        string? Message,
        double DurationMilliseconds,
        double FinishMilliseconds);
}

internal sealed record StreamingTranscriptionResult(
    string Text,
    string Language,
    double DurationMilliseconds,
    double FinishMilliseconds);
