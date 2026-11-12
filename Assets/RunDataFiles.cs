using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

public static class RunDataFiles
{
    // Deliberately whitelist this feature's files, not arbitrary project files or secrets.
    public static bool IsDataFile(string path)
    {
        string name = Path.GetFileName(path);
        return name == "metadata.json" || PartName(name,"commands",".jsonl") ||
            PartName(name,"robot-feedback",".csv") || PartName(name,"lift-feedback",".csv");
    }
    private static bool PartName(string name,string prefix,string extension)
    {
        if(name==prefix+extension) return true;
        if(!name.StartsWith(prefix+"_",StringComparison.Ordinal) || !name.EndsWith(extension,StringComparison.Ordinal)) return false;
        string number=name.Substring(prefix.Length+1,name.Length-prefix.Length-1-extension.Length);
        if(number.Length<4) return false;
        foreach(char c in number) if(c<'0' || c>'9') return false;
        return true;
    }
    public static Task<string> ExportAsync(string directory, string exportDirectory)
        => Task.Run(() =>
        {
            string source = Path.GetFullPath(directory);
            if (!File.Exists(Path.Combine(source,"metadata.json"))) throw new IOException("没有本版数据档案，不能导出");
            Directory.CreateDirectory(exportDirectory);
            string destination = Path.Combine(exportDirectory,"run-data-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip");
            string temporary = destination + ".partial";
            try
            {
                using (var output = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))
                using (var zip = new ZipArchive(output,ZipArchiveMode.Create))
                    foreach (var file in Directory.GetFiles(source))
                    {
                        if (!IsDataFile(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        var entry = zip.CreateEntry(Path.GetFileName(file),CompressionLevel.Fastest);
                        using (var input = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                        using (var target = entry.Open()) input.CopyTo(target);
                    }
                File.Move(temporary,destination);
                return destination;
            }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        });
}
