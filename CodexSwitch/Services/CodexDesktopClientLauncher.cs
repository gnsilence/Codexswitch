using System.ComponentModel;
using System.Diagnostics;

namespace CodexSwitch.Services;

public sealed class CodexDesktopClientLauncher
{
    public const string ProtocolUri = "codex://";
    private const string DesktopProcessName = "ChatGPT";
    private const int RestartWaitMilliseconds = 4000;

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

    public bool TryRestart()
    {
        return TryStop() && TryLaunch();
    }

    public bool TryStop()
    {
        var desktopProcesses = FindDesktopProcesses();
        try
        {
            foreach (var process in desktopProcesses)
            {
                if (HasExited(process))
                    continue;

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The desktop client may exit between inspection and termination.
                }
                catch (Win32Exception)
                {
                    // Keep checking the process below; a protected process must not
                    // be followed by launching another instance with stale config.
                }
                catch (NotSupportedException)
                {
                    // Process-tree termination is not supported on some platforms.
                    // The process state check below determines whether restart is safe.
                }
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(RestartWaitMilliseconds);
            while (DateTime.UtcNow < deadline &&
                   desktopProcesses.Any(process => !HasExited(process)))
            {
                Thread.Sleep(100);
            }

            if (desktopProcesses.Any(process => !HasExited(process)))
                return false;
        }
        catch (InvalidOperationException)
        {
            // The desktop client may exit while the process list is inspected.
            if (desktopProcesses.Any(process => !HasExited(process)))
                return false;
        }
        finally
        {
            foreach (var process in desktopProcesses)
                process.Dispose();
        }

        return true;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static List<Process> FindDesktopProcesses()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        return Process.GetProcessesByName(DesktopProcessName)
            .Where(IsCodexDesktopProcess)
            .ToList();
    }

    private static bool IsCodexDesktopProcess(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ||
                string.Equals(Path.GetFileName(path), "ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }
}
