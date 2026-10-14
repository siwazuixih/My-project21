using System;
using System.IO;
using System.Text;

// Bounded incremental evidence. No Unity calls: failures must never interrupt a stop request.
public sealed class CommissioningJournal : IDisposable
{
    private readonly string path;
    private readonly int limit;
    private StreamWriter writer;
    private int written;
    private bool disposed;
    public string Error { get; private set; }
    public CommissioningJournal(string directory, int characterLimit = 1000000)
    {
        path = Path.Combine(directory, "stop-diagnostic.log");
        limit = characterLimit;
    }
    public void Append(string line)
    {
        if (Error != null || disposed) return;
        try
        {
            if (line.Length > limit) line = "[line truncated] " + line.Substring(line.Length - limit / 2);
            if (written + line.Length + Environment.NewLine.Length > limit && writer != null)
            {
                writer.Dispose(); writer = null;
                string previous = path + ".previous";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
                written = 0;
            }
            if (writer == null) writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(line);
            written += line.Length + Environment.NewLine.Length;
        }
        catch (Exception e) { Error = e.Message; Dispose(); }
    }
    public void Dispose()
    {
        disposed = true;
        try { writer?.Dispose(); } catch (Exception e) { Error = e.Message; }
        writer = null;
    }
}
