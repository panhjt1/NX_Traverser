using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

// This UI thread NEVER calls NX APIs, logging, loading, rendering or cleanup.
// It stays responsive while the NX thread is inside a synchronous native call.
internal sealed class TraversalStopWindow : IDisposable
{
    private static int _liveThreads;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
    private volatile bool _closeRequested;
    private Exception _startupError;
    public static bool HasLiveWindow { get { return Volatile.Read(ref _liveThreads) != 0; } }

    public TraversalStopWindow()
    {
        _thread = new Thread(ShowWindow) { IsBackground = true, Name = "NX Traverser Stop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)) || _startupError != null)
        {
            Dispose();
            throw new InvalidOperationException("无法打开停止窗口，本次遍历未启动。", _startupError);
        }
    }

    private void ShowWindow()
    {
        Interlocked.Increment(ref _liveThreads);
        try
        {
            using (var form = new Form())
            using (var timer = new System.Windows.Forms.Timer())
            {
                form.Text = "NX Traverser — 遍历控制";
                form.ClientSize = new Size(360, 122);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false; form.MinimizeBox = false;
                form.TopMost = true;
                form.StartPosition = FormStartPosition.Manual;
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                form.Location = new Point(area.Right - form.Width - 20, area.Bottom - form.Height - 40);
                var label = new Label { Text = "遍历运行中。可停止后续组件的处理。", AutoSize = false,
                    Location = new Point(12, 10), Size = new Size(336, 48) };
                var stop = new Button { Text = "停止后续遍历", Location = new Point(90, 72), Size = new Size(180, 32) };
                form.Controls.Add(label); form.Controls.Add(stop);
                Action showStopping = () => {
                    stop.Enabled = false;
                    label.Text = "已请求停止。等待当前 NX 操作返回，\r\n然后恢复显示并清理临时部件。";
                };
                Action request = () => { AssemblyTraverser.RequestStop(); showStopping(); };
                stop.Click += (sender, args) => request();
                form.FormClosing += (sender, args) => {
                    if (!_closeRequested) { args.Cancel = true; request(); }
                };
                timer.Interval = 150;
                timer.Tick += (sender, args) => {
                    if (_closeRequested) form.Close();
                    else if (AssemblyTraverser.StopRequested) showStopping();
                };
                form.Shown += (sender, args) => { timer.Start(); _ready.Set(); };
                Application.Run(form);
                timer.Stop();
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            AssemblyTraverser.RequestStop();
        }
        finally
        {
            _ready.Set();
            Interlocked.Decrement(ref _liveThreads);
        }
    }

    public void Dispose()
    {
        _closeRequested = true;
        // No NX work or synchronous cross-thread Invoke can block this UI loop.
        // Do not unload the plugin while a window thread is still alive.
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("停止窗口未能关闭；已保留插件至 NX 退出，请查看日志。");
        _ready.Dispose();
    }
}
