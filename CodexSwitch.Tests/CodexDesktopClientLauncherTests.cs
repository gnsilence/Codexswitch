using System.ComponentModel;
using System.Diagnostics;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class CodexDesktopClientLauncherTests
{
    [Fact]
    public void TryLaunch_UsesRegisteredCodexProtocol()
    {
        ProcessStartInfo? captured = null;
        var launcher = new CodexDesktopClientLauncher(startInfo => captured = startInfo);

        Assert.True(launcher.TryLaunch());
        Assert.NotNull(captured);
        Assert.Equal(CodexDesktopClientLauncher.ProtocolUri, captured.FileName);
        Assert.True(captured.UseShellExecute);
    }

    [Fact]
    public void TryLaunch_ReturnsFalse_WhenProtocolIsUnavailable()
    {
        var launcher = new CodexDesktopClientLauncher(_ => throw new Win32Exception());

        Assert.False(launcher.TryLaunch());
    }
}
