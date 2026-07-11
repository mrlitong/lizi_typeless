using lizi_typeless.Core.Sessions;

namespace lizi_typeless.Core.Tests.Sessions;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "lizi_typeless.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveReplacesMetadataWithoutLeavingTemporaryFiles()
    {
        var store = new SessionStore(_directory);
        var startedAt = DateTimeOffset.UtcNow;
        var session = await store.CreateAsync(new TargetWindowInfo(42, 7, "Editor"), startedAt);
        session = SessionTransitions.MoveTo(session, SessionStatus.Processing, startedAt.AddSeconds(2)) with
        {
            RawTranscript = "hello",
            Timings = new SessionTimings(EndToTranscriptionMilliseconds: 123.4),
        };

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync(session.Id);

        Assert.Equal(SessionStatus.Processing, loaded.Status);
        Assert.Equal("hello", loaded.RawTranscript);
        Assert.Equal(123.4, loaded.Timings.EndToTranscriptionMilliseconds);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(store.ResolveFile(session, session.AudioFile))!,
            "*.tmp"));
    }

    [Fact]
    public async Task LoadAllReturnsNewestSessionFirst()
    {
        var store = new SessionStore(_directory);
        var first = await store.CreateAsync(
            new TargetWindowInfo(1, 1, "First"),
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var second = await store.CreateAsync(
            new TargetWindowInfo(2, 2, "Second"),
            DateTimeOffset.UtcNow);

        var sessions = await store.LoadAllAsync();

        Assert.Equal([second.Id, first.Id], sessions.Select(session => session.Id));
    }

    [Theory]
    [InlineData(SessionStatus.Failed, "", RetryStep.Transcribe)]
    [InlineData(SessionStatus.Failed, "raw text", RetryStep.Organize)]
    [InlineData(SessionStatus.Completed, "raw text", RetryStep.Organize)]
    public void RetryNeverAutomaticallyInserts(
        SessionStatus status,
        string rawTranscript,
        RetryStep expectedStep)
    {
        var session = new SessionRecord
        {
            Id = "session",
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = status,
            TargetWindow = new TargetWindowInfo(1, 1, "Editor"),
            RawTranscript = rawTranscript,
        };

        var retry = SessionTransitions.PlanRetry(session);

        Assert.Equal(expectedStep, retry.Step);
        Assert.False(retry.AutomaticallyInsert);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
