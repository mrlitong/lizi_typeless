namespace lizi_typeless.Core.Sessions;

public static class SessionTransitions
{
    public static bool CanMove(SessionStatus from, SessionStatus to) => (from, to) switch
    {
        (SessionStatus.Recording, SessionStatus.Processing) => true,
        (SessionStatus.Recording, SessionStatus.Failed) => true,
        (SessionStatus.Processing, SessionStatus.Ready) => true,
        (SessionStatus.Processing, SessionStatus.Failed) => true,
        (SessionStatus.Ready, SessionStatus.Completed) => true,
        (SessionStatus.Ready, SessionStatus.Processing) => true,
        (SessionStatus.Ready, SessionStatus.Failed) => true,
        (SessionStatus.Completed, SessionStatus.Processing) => true,
        (SessionStatus.Failed, SessionStatus.Processing) => true,
        _ when from == to => true,
        _ => false,
    };

    public static SessionRecord MoveTo(
        SessionRecord session,
        SessionStatus next,
        DateTimeOffset timestamp)
    {
        if (!CanMove(session.Status, next))
        {
            throw new InvalidOperationException(
                $"Session {session.Id} cannot move from {session.Status} to {next}.");
        }

        return session with
        {
            Status = next,
            UpdatedAt = timestamp,
        };
    }

    public static RetryPlan PlanRetry(SessionRecord session)
    {
        if (session.Status is not (SessionStatus.Failed or SessionStatus.Ready or SessionStatus.Completed))
        {
            throw new InvalidOperationException(
                $"Session {session.Id} cannot retry while in {session.Status}.");
        }

        return new RetryPlan(
            string.IsNullOrWhiteSpace(session.RawTranscript)
                ? RetryStep.Transcribe
                : RetryStep.Organize,
            AutomaticallyInsert: false);
    }
}
