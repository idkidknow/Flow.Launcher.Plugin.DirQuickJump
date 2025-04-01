using System;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.DirQuickJump.FileManager;

internal class Explorer : IFileManager
{
    public IEnumerable<Entry> GetEntries()
    {
        var shellApplicationType = Type.GetTypeFromProgID("Shell.Application", true)!;
        dynamic shellApplication = Activator.CreateInstance(shellApplicationType)!;
        foreach (dynamic window in shellApplication.Windows())
        {
            Entry? entry;
            try
            {
                string name = window.Document.Folder.Self.Name;
                string path = window.Document.Folder.Self.Path;
                entry = new Entry(name, path);
            }
            catch (Exception)
            {
                entry = null;
            }
            if (entry != null) yield return entry.Value;
        }
    }
}
