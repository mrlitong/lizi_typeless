using System.Text.Json.Serialization;

namespace lizi_typeless.Core.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<SessionStatus>))]
public enum SessionStatus
{
    Recording,
    Processing,
    Ready,
    Completed,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<FailureStage>))]
public enum FailureStage
{
    None,
    Recording,
    Transcription,
    Organization,
    TextInsertion,
    Recovery,
}

public sealed record TargetWindowInfo(long Handle, uint ProcessId, string Title);

public sealed record AudioFormatInfo(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string Encoding,
    int BlockAlign,
    int AverageBytesPerSecond);

public sealed record SessionTimings(
    double? CaptureStartMilliseconds = null,
    double? FirstAudioFrameMilliseconds = null,
    double? TranscriptionMilliseconds = null,
    double? OrganizationMilliseconds = null,
    double? TextInsertionMilliseconds = null,
    double? StreamingFinishMilliseconds = null,
    double? EndToTranscriptionMilliseconds = null,
    double? EndToOrganizationMilliseconds = null,
    double? EndToInsertionMilliseconds = null);

public sealed record SessionRecord
{
    public required string Id { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? StoppedAt { get; init; }

    public DateTimeOffset? InsertedAt { get; init; }

    public required SessionStatus Status { get; init; }

    public required TargetWindowInfo TargetWindow { get; init; }

    public string RawAudioFile { get; init; } = "audio.raw";

    public string AudioFile { get; init; } = "audio.wav";

    public AudioFormatInfo? AudioFormat { get; init; }

    public string RawTranscript { get; init; } = string.Empty;

    public string FinalText { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public FailureStage FailureStage { get; init; }

    public string Error { get; init; } = string.Empty;

    public SessionTimings Timings { get; init; } = new();

    public int RetryCount { get; init; }

    public bool WasAutomaticallyInserted { get; init; }
}

public enum RetryStep
{
    Transcribe,
    Organize,
}

public sealed record RetryPlan(RetryStep Step, bool AutomaticallyInsert);
