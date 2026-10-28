using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>Runs the actual panel with six disconnected sources; never reads/saves personal settings or starts adapters.</summary>
internal static class LoftReview
{
    public static int Run()
    {
        using var panel = new MonitorForm
        {
            Settings = new AppSettings { ShowPanelHeader = true, AlwaysOnTop = true },
            TopMost = true
        };
        panel.ShowData(MonitorContext.Collect([]));
        panel.ShutdownRequested += panel.Close;
        panel.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) panel.Close(); };
        panel.ShowNearTray();
        Application.Run(panel);
        return 0;
    }
}
