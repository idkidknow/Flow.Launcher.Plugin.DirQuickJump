using System.Collections.Generic;

namespace Flow.Launcher.Plugin.DirQuickJump.FileManager;

internal interface IFileManager
{
    IEnumerable<Entry> GetEntries();
}
