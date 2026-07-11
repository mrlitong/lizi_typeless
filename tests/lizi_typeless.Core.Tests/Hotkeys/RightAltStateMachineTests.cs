using lizi_typeless.Core.Hotkeys;

namespace lizi_typeless.Core.Tests.Hotkeys;

public sealed class RightAltStateMachineTests
{
    private static readonly DateTimeOffset Origin = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SingleIdlePressDoesNotStartRecording()
    {
        var machine = new RightAltStateMachine();

        var command = Press(machine, Origin);

        Assert.Equal(HotkeyCommand.None, command);
        Assert.Equal(CaptureState.Idle, machine.State);
    }

    [Fact]
    public void TwoIdlePressesInsideWindowStartOneRecording()
    {
        var machine = new RightAltStateMachine();

        Press(machine, Origin);
        var command = Press(machine, Origin.AddMilliseconds(300));

        Assert.Equal(HotkeyCommand.StartRecording, command);
        Assert.Equal(CaptureState.Recording, machine.State);
    }

    [Fact]
    public void HeldKeyRepeatDoesNotCountAsSecondPress()
    {
        var machine = new RightAltStateMachine();

        Assert.Equal(HotkeyCommand.None, machine.OnKeyDown(Origin));
        Assert.Equal(HotkeyCommand.None, machine.OnKeyDown(Origin.AddMilliseconds(50)));
        machine.OnKeyUp();

        Assert.Equal(CaptureState.Idle, machine.State);
    }

    [Fact]
    public void FirstPressWhileRecordingStopsExactlyOnce()
    {
        var machine = StartRecording();
        var stopTime = Origin.AddSeconds(1);

        Assert.Equal(HotkeyCommand.StopRecording, machine.OnKeyDown(stopTime));
        Assert.Equal(HotkeyCommand.None, machine.OnKeyDown(stopTime.AddMilliseconds(20)));
        machine.OnKeyUp();
        Assert.Equal(HotkeyCommand.None, Press(machine, stopTime.AddMilliseconds(100)));
        Assert.Equal(CaptureState.Processing, machine.State);
    }

    [Fact]
    public void StopSuppressionSurvivesFastProcessingCompletion()
    {
        var machine = StartRecording();
        var stopTime = Origin.AddSeconds(1);
        Press(machine, stopTime);
        machine.MarkProcessingFinished();

        Assert.Equal(HotkeyCommand.None, Press(machine, stopTime.AddMilliseconds(200)));
        Assert.Equal(HotkeyCommand.None, Press(machine, stopTime.AddMilliseconds(800)));
        Assert.Equal(HotkeyCommand.StartRecording, Press(machine, stopTime.AddMilliseconds(1000)));
    }

    private static RightAltStateMachine StartRecording()
    {
        var machine = new RightAltStateMachine();
        Press(machine, Origin);
        Assert.Equal(HotkeyCommand.StartRecording, Press(machine, Origin.AddMilliseconds(200)));
        return machine;
    }

    private static HotkeyCommand Press(RightAltStateMachine machine, DateTimeOffset timestamp)
    {
        var command = machine.OnKeyDown(timestamp);
        machine.OnKeyUp();
        return command;
    }
}
