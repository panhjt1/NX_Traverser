using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NXOpen;

internal sealed class RunLog : IDisposable
{
    internal const string DiagnosticRevision = "nx2512-dlx-rename-20261104";
    private static RunLog _current;
    private static int _callId;
    private readonly StreamWriter _writer;
    private readonly Session _session;
    public string FilePath { get; private set; }

    public RunLog(Session session, string directory)
    {
        _session = session;
        FilePath = Path.Combine(directory, "Traversal.log");
        _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
        _writer.AutoFlush = true;
        _callId = 0;
        _current = this;
        Write("[Run started] " + DateTime.Now.ToString("O"));
    }

    public static void Write(string message)
    {
        if (_current == null) return;
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message;
        _current._writer.WriteLine(line);
    }

    // The driver calls this once, after traversal, restoration, unloading and
    // every handler's Finish. Detailed progress never goes to the NX window.
    public void ShowSummary(IList<string> lines)
    {
        foreach (string line in lines) Write("[Summary] " + line);
        try
        {
            _session.ListingWindow.Open();
            foreach (string line in lines) _session.ListingWindow.WriteLine(line);
        }
        catch (Exception ex) { Error("显示执行汇总失败（结果已写入文件日志）", ex); }
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
        catch (Exception ex)
        {
            // Preserve the failing call and full exception even when an outer
            // layer subsequently encounters a different restoration error.
            if (ex is OperationCanceledException)
                Write("[NX call canceled] #" + id + " " + stage + ": " + ex.Message);
            else Error("NX call #" + id + " " + stage, ex);
            throw;
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
