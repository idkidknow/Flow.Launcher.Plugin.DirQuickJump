using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

namespace Flow.Launcher.Plugin.DirQuickJump.FileManager;

internal class DirectoryOpus : IFileManager
{
    public IEnumerable<Entry> GetEntries()
    {
        var processes = Process.GetProcessesByName("dopus");
        if (!processes.Any()) return [];
        var process = processes.First();
        string? dopusPath = process.MainModule?.FileName;
        if (dopusPath == null) return [];
        string? parent = Directory.GetParent(dopusPath)?.FullName;
        if (parent == null) return [];
        string rtPath = Path.Combine(parent, "dopusrt.exe");
        string pathsXml = Path.Combine(Path.GetTempPath(), "DirQuickJump_dopus_paths.xml");
        File.Delete(pathsXml);
        Process.Start(rtPath, ["/info", $"{pathsXml},paths"]).WaitForExit();
        var doc = new XmlDocument();
        doc.Load(pathsXml);
        string? activePath = doc.SelectSingleNode("/results/path[@active_tab='1']")?.InnerText;
        var otherPaths = doc.SelectNodes("/results/path[not(@active_tab)]")?
            .Cast<XmlNode>()
            .Select(node => node.InnerText) ?? [];
        var paths = activePath != null ? new List<string> { activePath }.Concat(otherPaths) : otherPaths;
        return paths.Select(path =>
        {
            string name = Path.GetFileName(path);
            return new Entry(name, path);
        });
    }
}