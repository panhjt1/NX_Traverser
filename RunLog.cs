using System;
using System.IO;
using System.Text;
using NXOpen;

internal sealed class RunLog : IDisposable
{
    private static RunLog _current;
    private readonly StreamWriter _writer;
    private readonly Session _session;

    public RunLog(Session session, string directory)
    {
        _session = session;
        session.ListingWindow.Open();
        _writer = new StreamWriter(Path.Combine(directory, "Traversal.log"), false, new UTF8Encoding(true));
        _writer.AutoFlush = true;
        _current = this;
    }

    public static void Write(string message)
    {
        if (_current == null) return;
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message;
        _current._writer.WriteLine(line);
        _current._session.ListingWindow.WriteLine(line);
    }

    public static void Error(string stage, Exception ex)
    {
        var nx = ex as NXException;
        Write("[ERROR] " + stage + (nx == null ? "" : " NXError=" + nx.ErrorCode) + ": " + ex.Message);
    }

    public void Dispose()
    {
        _current = null;
        _writer.Dispose();
    }
}
