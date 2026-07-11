using System.Text.Json;
using lizi_typeless.Windows.Inference;

namespace lizi_typeless.Windows.Tests.Inference;

public sealed class InferenceContractTests
{
    [Fact]
    public void OrganizationRequestUsesPythonFieldNames()
    {
        var json = JsonSerializer.Serialize(
            new OrganizationRequest("hello"),
            InferenceClient.JsonOptions);

        Assert.Equal("{\"text\":\"hello\"}", json);
    }

    [Fact]
    public void HealthResponseReadsSnakeCaseFields()
    {
        const string Json = """
            {
              "status": "ready",
              "ready": true,
              "asr_model": "Qwen3-ASR-1.7B",
              "organizer_model": "Qwen3-1.7B",
              "device": "NVIDIA GeForce RTX 5090",
              "streaming": true
            }
            """;

        var health = JsonSerializer.Deserialize<InferenceHealth>(Json, InferenceClient.JsonOptions);

        Assert.NotNull(health);
        Assert.Equal("Qwen3-ASR-1.7B", health.AsrModel);
        Assert.Equal("Qwen3-1.7B", health.OrganizerModel);
        Assert.True(health.Streaming);
    }

    [Fact]
    public void InferenceResponsesReadSnakeCaseTimings()
    {
        const string TranscriptionJson =
            "{\"text\":\"hello\",\"language\":\"Chinese\",\"duration_milliseconds\":12.5}";
        const string OrganizationJson =
            "{\"text\":\"hello\",\"duration_milliseconds\":7.25}";

        var transcription = JsonSerializer.Deserialize<TranscriptionResult>(
            TranscriptionJson,
            InferenceClient.JsonOptions);
        var organization = JsonSerializer.Deserialize<OrganizationResult>(
            OrganizationJson,
            InferenceClient.JsonOptions);

        Assert.NotNull(transcription);
        Assert.Equal(12.5, transcription.DurationMilliseconds);
        Assert.NotNull(organization);
        Assert.Equal(7.25, organization.DurationMilliseconds);
    }

    [Fact]
    public void ErrorSummaryOmitsEchoedUserInput()
    {
        const string ErrorJson = """
            {
              "detail": [
                {
                  "type": "missing",
                  "loc": ["body", "text"],
                  "msg": "Field required",
                  "input": {"Text": "private spoken text"}
                }
              ]
            }
            """;

        var summary = InferenceClient.SummarizeErrorResponse(ErrorJson);

        Assert.Contains("missing at body.text: Field required", summary);
        Assert.DoesNotContain("private spoken text", summary);
    }
}
