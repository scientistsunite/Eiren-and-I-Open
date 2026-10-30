using BCC.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;

namespace BCC.App;

internal sealed class MainWindow : Form
{
    private readonly MonitoringSession _session = new(new AgentCommands.WindowsEnvironment());
    private DeviceInfo _device => _session.Device;
    private readonly IChargingController? _controller;
    private readonly Label _machine = MakeLabel(10), _battery = MakeLabel(10), _control = MakeLabel(10), _saved = MakeLabel(10);
    private readonly Label _risk = MakeLabel(10);
    private readonly ComboBox _limits = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly Button _apply = MakeButton("Apply charging limit"), _restore = MakeButton("Restore original settings"), _prepare = MakeButton("Prepare for another OS"),
        _resume = MakeButton("Resume saved setting"), _recheck = MakeButton("Recheck compatibility"), _export = MakeButton("Export compatibility report");
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 60_000 };
    private readonly CheckBox _acknowledge = new() { Text = "I have read and understand the cross-boot limitations", AutoSize = true };
    private ControllerProbe? _probe;

    private const string CrossBootDisclosure = SafetyDisclosure.CrossBoot;

    public MainWindow()
    {
        Text = "Battery Charge Control — Windows v0.1";
        Size = new Size(810, 775);
        MinimumSize = new Size(660, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        Icon = MakeIcon();
        _controller = _session.Controller;

        var menu = new MenuStrip();
        var appMenu = new ToolStripMenuItem("Application");
        appMenu.DropDownItems.Add("Modify installation...", null, (_, _) => InstallerAgain());
        appMenu.DropDownItems.Add("Repair installation...", null, (_, _) => InstallerAgain());
        appMenu.DropDownItems.Add(new ToolStripSeparator());
        appMenu.DropDownItems.Add("Uninstall...", null, (_, _) => Uninstall());
        var helpMenu = new ToolStripMenuItem("Help");
        helpMenu.DropDownItems.Add("Important cross-boot information", null, (_, _) => ShowDisclosure());
        helpMenu.DropDownItems.Add("Manufacturer driver information", null, (_, _) => OpenManufacturerLink());
        menu.Items.Add(appMenu); menu.Items.Add(helpMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(19, 12, 19, 17), AutoScroll = true, AutoSize = false, ColumnCount = 1, RowCount = 8 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 111));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(body);
        body.BringToFront();
        menu.BringToFront();
        var title = MakeLabel(18, true); title.Text = "Battery Charge Control"; title.ForeColor = Color.FromArgb(25, 52, 67);
        body.Controls.Add(title, 0, 0);
        body.Controls.Add(_machine, 0, 1); body.Controls.Add(_battery, 0, 2); body.Controls.Add(_control, 0, 3);
        var chooser = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false, Padding = new Padding(0, 2, 0, 0) };
        chooser.Controls.Add(new Label { Text = "Requested charging limit:", AutoSize = true, Padding = new Padding(0, 7, 8, 0) });
        chooser.Controls.Add(_limits); chooser.Controls.Add(_apply);
        _limits.SelectedIndexChanged += (_, _) => _apply.Enabled = _session.CanRequestLimit && _limits.SelectedItem is int;
        body.Controls.Add(chooser, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.Add(_restore); actions.Controls.Add(_prepare); actions.Controls.Add(_resume);
        body.Controls.Add(actions, 0, 5);
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        info.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _risk.Text = "Cross-boot restoration cannot be guaranteed. Read the important information before enabling control.";
        _risk.ForeColor = Color.FromArgb(125, 64, 19);
        info.Controls.Add(_risk, 0, 0);
        info.Controls.Add(_saved, 0, 1);
        var smallActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        smallActions.Controls.Add(_recheck); smallActions.Controls.Add(_export);
        info.Controls.Add(smallActions, 0, 2);
        body.Controls.Add(info, 0, 6);
        body.Controls.Add(_details, 0, 7);

        _tray = new NotifyIcon { Icon = Icon, Visible = true, Text = "Battery Charge Control" };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Battery Charge Control", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("Restore original settings...", null, (_, _) => { ShowWindow(); _restore.PerformClick(); });
        trayMenu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = trayMenu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); } };
        FormClosed += (_, _) => { _tray.Visible = false; _tray.Dispose(); _refreshTimer.Dispose(); };
        _refreshTimer.Tick += (_, _) => ShowBattery();
        _refreshTimer.Start();

        _apply.Click += async (_, _) =>
        {
            if (_limits.SelectedItem is not int limit) return;
            if (!ShowDisclosure(askConsent: true)) return;
            await RunPrivileged("--set", limit.ToString());
        };
        _restore.Click += async (_, _) => await RunPrivileged("--restore");
        _prepare.Click += async (_, _) => await RunPrivileged("--prepare");
        _resume.Click += async (_, _) =>
        {
            if (!ShowDisclosure(askConsent: true)) return;
            await RunPrivileged("--resume");
        };
        _recheck.Click += (_, _) => RefreshAll();
        _export.Click += (_, _) => ExportReport();
        RefreshAll();
    }

    private void ShowBattery()
    {
        var snapshot = _session.RefreshBattery();
        _apply.Enabled = _session.CanRequestLimit && _limits.SelectedItem is int;
        _restore.Enabled = _prepare.Enabled = _session.CanRestore;
        _resume.Enabled = _session.CanResume;
        _battery.Text = snapshot.Present
            ? $"Battery: {snapshot.Percent?.ToString() ?? "?"}%   •   {snapshot.Power}   •   {snapshot.Status}"
            : "Battery: No internal battery detected — monitoring-only mode";
        _tray.Text = snapshot.Percent is int p ? $"Battery Charge Control — {p}%" : "Battery Charge Control";
    }

    private void RefreshAll()
    {
        _machine.Text = $"Computer: {_device.Manufacturer}  {_device.Model}   •   BIOS: {_device.BiosVersion}   •   {_device.Architecture}";
        ShowBattery();
        _session.Refresh();
        _probe = _session.Probe;
        _limits.Items.Clear();
        if (_session.CanRequestLimit)
        {
            foreach (var limit in _probe!.CandidateLimits) _limits.Items.Add(limit);
        }
        _apply.Enabled = false;
        _control.Text = _probe is null ? "Charging control: Not installed for this manufacturer (monitoring available)" :
            $"Charging control: {_probe.Availability} — Current firmware readback: {_probe.CurrentLimit?.ToString() ?? "unknown"}%";
        MachinePolicy? policy = null;
        string policyInfo;
        try
        {
            policy = _session.Policy;
            if (_session.PolicyError is not null) throw new InvalidDataException(_session.PolicyError);
            if (policy is null) throw new InvalidDataException("Policy unavailable");
            policyInfo = $"Saved for this Windows installation: {(policy.SelectedLimit is int x ? x + "%" : "none")}  •  " +
                $"Original: {(policy.OriginalLimit is int y ? y + "%" : "unknown")}  •  " +
                $"Automatic reapplication: {(policy.Enabled ? "enabled" : "disabled")}  •  " +
                $"Restoration required: {(policy.RestorationRequired ? "yes" : "no")}  •  Maintenance: {(policy.MaintenancePending ? "pending — rerun Setup/Repair" : "complete")}";
        }
        catch (Exception ex) { policyInfo = "Policy unavailable: " + ex.Message; }
        _saved.Text = policyInfo;
        _risk.Text = _session.Policy?.MaintenancePending == true
            ? "Installation maintenance is pending. Charging changes are paused; use Restore or rerun Setup/Repair."
            : _session.Policy is { Enabled: false, RestorationRequired: true }
            ? "Restoration is still required. Use Restore original settings; automatic reapplication is disabled."
            : "Cross-boot restoration cannot be guaranteed. Read the important information before enabling control.";
        _restore.Enabled = _prepare.Enabled = _session.CanRestore;
        _resume.Enabled = _session.CanResume;
        var sb = new StringBuilder();
        sb.AppendLine("BATTERY CHARGE CONTROL — COMPATIBILITY REPORT (v0.1)");
        sb.AppendLine();
        sb.AppendLine("Manufacturer: " + _device.Manufacturer);
        sb.AppendLine("Model: " + _device.Model);
        sb.AppendLine("Controller family: " + _device.Brand);
        sb.AppendLine("BIOS version: " + _device.BiosVersion);
        sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
        sb.AppendLine("Processor architecture: " + _device.Architecture);
        sb.AppendLine("Installed controller: " + (_controller?.GetType().FullName ?? "None; generic monitoring only"));
        sb.AppendLine("Controller status: " + (_probe?.Availability.ToString() ?? "Unsupported"));
        sb.AppendLine("Controller details: " + (_probe?.Description ?? "No matching controller module installed."));
        sb.AppendLine("Firmware readback: " + (_probe?.CurrentLimit?.ToString() ?? "Unknown"));
        sb.AppendLine("Candidate limits (NOT hardware validated): " + (_probe is null ? "None" : string.Join(", ", _probe.CandidateLimits)));
        bool serviceInstalled = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BatteryChargeControlAgent", "ImagePath", null) != null;
        sb.AppendLine("Shutdown restoration agent installed: " + (serviceInstalled ? "Yes (best-effort)" : "No — automatic shutdown restoration NOT provided"));
        sb.AppendLine("Physical charging cutoff: NOT VERIFIED");
        sb.AppendLine("Startup/shutdown isolation: BEST-EFFORT ONLY; NEVER GUARANTEED");
        sb.AppendLine("Policy: " + policyInfo);
        sb.AppendLine();
        sb.AppendLine(CrossBootDisclosure);
        _details.Text = sb.ToString();
    }

    private async Task RunPrivileged(params string[] args)
    {
        _session.OperationInProgress = true;
        _apply.Enabled = _restore.Enabled = _prepare.Enabled = _resume.Enabled = false;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("Operation was not started");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                MessageBox.Show("The operation did not complete successfully. No success is claimed. " +
                    "Inspect the agent log in the application's compatibility folder or consult the report.",
                    "Operation not confirmed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show("Command completed. Where supported, firmware readback was checked. " +
                    "Actual battery charge-cutoff behavior is not established by readback alone.", "Result", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { /* UAC declined; no change */ }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Operation not completed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _session.OperationInProgress = false; RefreshAll(); }
    }

    private bool ShowDisclosure(bool askConsent = false)
    {
        if (!askConsent)
        {
            MessageBox.Show(CrossBootDisclosure, "Important boot-source limitations", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        bool agentInstalled = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BatteryChargeControlAgent", "ImagePath", null) != null;
        string extra = agentInstalled ? "" : "\r\n\r\nWARNING: The optional preshutdown agent is NOT installed. Automatic restoration during Windows shutdown will NOT be attempted.\r\n";
        return MessageBox.Show(CrossBootDisclosure + extra + "\r\n\r\nDo you understand and wish to proceed with this action?",
            "Consent required before firmware changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void ExportReport()
    {
        using var save = new SaveFileDialog { FileName = "Battery-Charge-Control-Compatibility.txt", Filter = "Text document (*.txt)|*.txt" };
        if (save.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(save.FileName, _details.Text, Encoding.UTF8);
            MessageBox.Show("Compatibility report saved. Check the report before sharing it.");
        }
    }

    private void InstallerAgain()
    {
        MessageBox.Show("To modify or repair installation, run the universal Battery Charge Control installer again. " +
                        "Select the needed manufacturer and components. Please select your installer file in the next dialog.",
                        "Modify or repair", MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var pick = new OpenFileDialog { Title = "Select Battery Charge Control Setup", Filter = "Executable installer (*.exe)|*.exe" };
        if (pick.ShowDialog(this) == DialogResult.OK)
            Process.Start(new ProcessStartInfo(pick.FileName) { UseShellExecute = true, Verb = "runas" });
    }

    private void Uninstall()
    {
        if (MessageBox.Show("Uninstall Battery Charge Control? The uninstaller will try to restore " +
                "the original firmware charging limit before removal. Restoration is not guaranteed; check " +
                "the hardware afterward. Removal stops if recovery or service cleanup cannot be verified. " +
                "Original configuration and recovery information are retained.", "Uninstall",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        string path = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(path))
        {
            MessageBox.Show("Uninstaller not found here. Use Windows Settings → Apps → Installed apps.");
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
        Close();
    }

    private void OpenManufacturerLink()
    {
        string url = _device.Brand switch
        {
            "ASUS" => "https://www.asus.com/support/download-center/",
            "Lenovo" => "https://pcsupport.lenovo.com/",
            "Dell" => "https://www.dell.com/support/home/",
            "HP" => "https://support.hp.com/",
            "MSI" => "https://www.msi.com/support",
            "Acer" => "https://www.acer.com/support",
            "Samsung" => "https://www.samsung.com/support/",
            "Microsoft" => "https://support.microsoft.com/surface",
            "LG" => "https://www.lg.com/support",
            "Huawei" => "https://consumer.huawei.com/support/",
            "Xiaomi" => "https://www.mi.com/global/support/",
            "Razer" => "https://mysupport.razer.com/",
            "Gigabyte" => "https://www.gigabyte.com/Support",
            "Framework" => "https://frame.work/support",
            "Dynabook" => "https://support.dynabook.com/",
            _ => "https://support.microsoft.com/windows"
        };
        if (MessageBox.Show($"Open the official { _device.Brand } support page in your browser?\r\n{url}",
                "External website", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static Label MakeLabel(float pt, bool bold = false) => new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular)
    };
    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, Height = 29, Margin = new Padding(3) };
    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(30, 55, 67), 3);
            graphics.DrawRectangle(pen, 5, 7, 22, 19);
            using var fill = new SolidBrush(Color.FromArgb(30, 55, 67));
            graphics.FillRectangle(fill, 28, 13, 3, 7);
            graphics.DrawLine(pen, 10, 16, 23, 16);
        }
        IntPtr hicon = bitmap.GetHicon();
        using var icon = Icon.FromHandle(hicon);
        var copy = (Icon)icon.Clone();
        DestroyIcon(hicon);
        return copy;
    }
}
