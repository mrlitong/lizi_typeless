using System.Net.Http.Json;
using System.Text.Json;

namespace lizi_typeless.Windows.Inference;

internal sealed class InferenceClient : IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;

    public InferenceClient(Uri baseAddress)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    public async Task<InferenceHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("v1/health", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<InferenceHealth>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The inference health response was empty.");
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        await using var audio = File.OpenRead(audioPath);
        using var form = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "audio", Path.GetFileName(audioPath));
        form.Add(new StringContent(preview ? "true" : "false"), "preview");

        using var response = await _httpClient.PostAsync("v1/transcribe", form, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<TranscriptionResult>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The transcription response was empty.");
    }

    public async Task<OrganizationResult> OrganizeAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
                "v1/organize",
                new OrganizationRequest(text),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<OrganizationResult>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The organization response was empty.");
    }

    public async Task RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("v1/shutdown", content: null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _httpClient.Dispose();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = SummarizeErrorResponse(body);
        throw new HttpRequestException(
            $"Inference request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {detail}",
            inner: null,
            response.StatusCode);
    }

    internal static string SummarizeErrorResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("detail", out var detail))
            {
                return "Response details omitted.";
            }

            if (detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? "Response details omitted.";
            }

            if (detail.ValueKind != JsonValueKind.Array)
            {
                return "Response details omitted.";
            }

            var errors = detail.EnumerateArray().Select(error =>
            {
                var type = error.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                var message = error.TryGetProperty("msg", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                var location = error.TryGetProperty("loc", out var locationElement) &&
                    locationElement.ValueKind == JsonValueKind.Array
                        ? string.Join('.', locationElement.EnumerateArray().Select(item => item.ToString()))
                        : "unknown";
                return $"{type ?? "error"} at {location}: {message ?? "request rejected"}";
            });
            return string.Join("; ", errors);
        }
        catch (JsonException)
        {
            return "Response details omitted.";
        }
    }
}

internal sealed record InferenceHealth(
    string Status,
    bool Ready,
    string AsrModel,
    string OrganizerModel,
    string Device,
    bool Streaming);

internal sealed record TranscriptionResult(string Text, string Language, double DurationMilliseconds);

internal sealed record OrganizationRequest(string Text);

internal sealed record OrganizationResult(string Text, double DurationMilliseconds);
