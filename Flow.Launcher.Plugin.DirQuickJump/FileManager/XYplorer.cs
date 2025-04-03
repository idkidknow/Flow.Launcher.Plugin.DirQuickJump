using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;

namespace Flow.Launcher.Plugin.DirQuickJump.FileManager;

internal class XYplorer : IFileManager
{
    public IEnumerable<Entry> GetEntries()
    {
        if (!Process.GetProcessesByName("XYplorer").Any()) return [];
        return GetHandles().SelectMany(GetPaths).Select(path =>
            new Entry(Path.GetFileName(path), path)
        );
    }

    private static List<HWND> GetHandles()
    {
        List<HWND> handles = [];

        PInvoke.EnumWindows(EnumProc, 0);
        return handles;

        BOOL EnumProc(HWND hWnd, LPARAM _)
        {
            string? cls = Utils.GetClassName(hWnd);
            if (cls == "ThunderRT6FormDC")
            {
                handles.Add(hWnd);
            }

            return true;
        }
    }

    private static unsafe List<string> GetPaths(HWND hWnd)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "DirQuickJump_xy_paths.txt")
            .Replace(@"\", @"\\");
        var script = $$"""
                       ::
                           explode($paths, "<get Tabs_sf>");
                           foreach($paths as $i => $path) {
                               $paths[$i] = pathreal($path);
                           }
                           writefile("{{tempFile}}", implode($paths));
                       """;
        
        // see `copydata` in XYplorer's Scripting Commands Reference
        byte[] scriptBytes = Encoding.Unicode.GetBytes(script);
        fixed (byte* dataPtr = scriptBytes)
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = 0x00400001,
                cbData = (uint)scriptBytes.Length,
                lpData = dataPtr,
            };
            PInvoke.SendMessage(hWnd, PInvoke.WM_COPYDATA, 0, new IntPtr(&cds));
        }

        try
        {
            string paths = File.ReadAllText(tempFile);
            return paths.Split("|").ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}