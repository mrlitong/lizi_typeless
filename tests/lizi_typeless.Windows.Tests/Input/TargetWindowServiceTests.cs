using lizi_typeless.Core.Sessions;
using lizi_typeless.Windows.Input;

namespace lizi_typeless.Windows.Tests.Input;

public sealed class TargetWindowServiceTests
{
    [Fact]
    public void ApplicationWindowIsNeverAnAutomaticInsertionTarget()
    {
        var target = new TargetWindowInfo(1, (uint)Environment.ProcessId, "lizi_typeless History");

        Assert.False(TargetWindowService.IsStillForeground(target));
    }
}
