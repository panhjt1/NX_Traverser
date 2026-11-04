using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NXOpen;

internal sealed class RunLog : IDisposable
{
    internal const string DiagnosticRevision = "nx2512-measure-cleanup-20261104";
    private static RunLog _current;
    private static int _callId;
    private readonly StreamWriter _writer;
    private readonly Session _session;

    public RunLog(Session session, string directory)
    {
        _session = session;
        session.ListingWindow.Open();
        _writer = new StreamWriter(Path.Combine(directory, "Traversal.log"), false, new UTF8Encoding(true));
        _writer.AutoFlush = true;
        _callId = 0;
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
        // Keep the information window concise; preserve stack/inner exceptions
        // in the already auto-flushed file for the local SDK agent.
        if (_current != null) _current._writer.WriteLine("[Exception detail] " + stage + Environment.NewLine + ex);
    }

    public static void WriteBuildIdentity()
    {
        var assembly = typeof(AssemblyTraverser).Assembly;
        Write("[Build] " + DiagnosticRevision + " MVID=" + assembly.ManifestModule.ModuleVersionId);
        Write("[Assembly] " + assembly.Location);
    }

    // All NX calls remain on the calling NX thread. A paired record identifies
    // calls blocked by loading or a modal UI; elapsed time alone is not proof of
    // a dialog. Always record an end, including an API/status-check exception.
    public static void NxCall(string stage, Action action)
    {
        int id = ++_callId;
        var timer = Stopwatch.StartNew();
        bool returned = false;
        Write("[NX call begin] #" + id + " " + stage);
        try
        {
            action();
            returned = true;
        }
        finally
        {
            Write("[NX call end] #" + id + " " + stage + " Completed=" + returned +
                " ElapsedMs=" + timer.ElapsedMilliseconds);
        }
    }

    public void Dispose()
    {
        _current = null;
        _writer.Dispose();
    }
}
