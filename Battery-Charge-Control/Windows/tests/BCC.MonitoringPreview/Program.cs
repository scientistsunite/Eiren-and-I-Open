using BCC.Core;
using BCC.Simulation;

namespace BCC.MonitoringPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!SimulationEntry.Accepts(args)) return 2;
        ApplicationConfiguration.Initialize();
        var host = new SimulationEnvironment();
        var session = new MonitoringSession(host);
        using var window = new Form { Text = "BCC — SIMULATION ONLY — no hardware access", Width = 850, Height = 700 };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        var refresh = new Button { Text = "Refresh simulated monitoring", Dock = DockStyle.Top, Height = 40 };
        void Refresh()
        {
            session.Refresh();
            text.Text = $"SIMULATION ONLY. No real battery data or charging controls.\r\n\r\n" +
                $"Simulated manufacturer: {session.Device.Manufacturer}\r\n" +
                $"Simulated battery: {session.Battery.Percent}% — {session.Battery.Status}\r\n" +
                $"Simulated classification: {session.Availability}\r\n\r\n" + SafetyDisclosure.CrossBoot +
                "\r\n\r\nRemove this separate preview using Windows Settings → Apps → Installed apps. " +
                "It does not exercise the production GUI, installer actions or hardware.";
        }
        refresh.Click += (_, _) => Refresh();
        window.Controls.Add(text); window.Controls.Add(refresh);
        Refresh();
        Application.Run(window);
        return 0;
    }
}
