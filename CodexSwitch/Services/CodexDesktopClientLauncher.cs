using System.ComponentModel;
using System.Diagnostics;

namespace CodexSwitch.Services;

public sealed class CodexDesktopClientLauncher
{
    public const string ProtocolUri = "codex://";

    private readonly Action<ProcessStartInfo> _startProcess;

    public CodexDesktopClientLauncher(Action<ProcessStartInfo>? startProcess = null)
    {
        _startProcess = startProcess ?? (startInfo => Process.Start(startInfo)?.Dispose());
    }

    public bool TryLaunch()
    {
        try
        {
            _startProcess(new ProcessStartInfo(ProtocolUri)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
