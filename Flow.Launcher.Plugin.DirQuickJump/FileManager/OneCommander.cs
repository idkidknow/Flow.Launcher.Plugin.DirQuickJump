using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;

namespace Flow.Launcher.Plugin.DirQuickJump.FileManager;

internal class OneCommander : IFileManager
{
    public IEnumerable<Entry> GetEntries()
    {
        var procs = Process.GetProcessesByName("OneCommander");
        if (!procs.Any()) return [];
        var proc = procs[0]; // Singleton
        int pid = proc.Id;
        return GetWindows(pid).SelectMany(GetPaths).Select(path =>
            new Entry(Path.GetFileName(path), path)
        );
    }

    private static List<HWND> GetWindows(int pid)
    {
        List<HWND> handles = [];

        PInvoke.EnumWindows(EnumProc, 0);
        return handles;

        unsafe BOOL EnumProc(HWND hWnd, LPARAM _)
        {
            if (!PInvoke.IsWindowVisible(hWnd)) return true;
            uint currentPid;
            uint threadId = PInvoke.GetWindowThreadProcessId(hWnd, &currentPid);
            if (threadId != 0 && pid == currentPid)
            {
                handles.Add(hWnd);
            }
            return true;
        }
    }

    private static List<string> GetPaths(HWND hWnd)
    {
        using var automation = new UIA3Automation();
        var window = automation.FromHandle(hWnd);
        string? path;
        try
        {
            path = window.FindFirstChild(cf => cf.ByAutomationId("CurrentPathGet"))?.AsTextBox()?.Text;
        }
        catch (MethodNotSupportedException)
        {
            path = null;
        }
        return path != null ? [path] : [];
    }
}
