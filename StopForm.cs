// =============================================================================
// StopForm.cs - 运行控制窗口
// 提供停止按钮，可在遍历过程中随时停止程序
// =============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// 运行控制窗口
/// 显示"停止运行"按钮，用于在遍历过程中终止程序
/// </summary>
public class StopForm : Form
{
    private Button stopButton;
    private Label statusLabel;

    /// <summary>
    /// 静态停止标记（volatile 保证多线程可见性）
    /// </summary>
    public static volatile bool StopRequested = false;

    public StopForm()
    {
        this.Text = "运行控制";
        this.Size = new Size(300, 120);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.TopMost = true;   // 始终保持窗口在最前

        stopButton = new Button
        {
            Text = "停止运行",
            Size = new Size(100, 40),
            Location = new Point(100, 30)
        };
        stopButton.Click += (s, e) =>
        {
            StopRequested = true;
            statusLabel.Text = "正在停止...";
        };

        statusLabel = new Label
        {
            Text = "运行中...",
            AutoSize = true,
            Location = new Point(20, 80)
        };

        this.Controls.Add(stopButton);
        this.Controls.Add(statusLabel);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 仅仅隐藏窗口而不关闭，避免误关闭后无法再停止
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
        base.OnFormClosing(e);
    }
}
