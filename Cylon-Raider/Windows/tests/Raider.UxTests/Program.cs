using Raider.App;
using Raider.Core;
using Raider.History;
using Raider.Service;
using System.Reflection;

internal static class Program
{
    static int checks;
    static readonly DateTime Epoch = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    static void Check(bool condition, string explanation)
    {
        if (!condition) throw new InvalidOperationException(explanation);
        checks++;
        Console.WriteLine("PASS " + explanation);
    }
    static Measurement Frame(int second, params Row[] rows) => new(1, Epoch.AddSeconds(second), 1, true, 0, 0, rows, "Synthetic UX fixture") { SourceSession = "synthetic-session" };
    static Row ApplicationRow(int index, long incoming = 10, long outgoing = 3) => new(new Identity(index + 100, Epoch.Ticks + index, "Synthetic application " + index, @"C:\Synthetic\application-" + index + ".exe"), incoming, outgoing);

    [STAThread]
    static int Main()
    {
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("UX regression deadline exceeded"); Environment.Exit(1); }, null, TimeSpan.FromMinutes(2), Timeout.InfiniteTimeSpan);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            IndexRegressions();
            GridRegressions();
            UnifiedShellRegressions();
            Console.WriteLine($"PASS {checks} UX regressions. Session data is synthetic; the unified shell opens only its newly created disposable portable settings/history. No collector was started, no service or registry configuration was changed, and no user data was opened. Font-scale layout simulation is not physical Windows DPI acceptance.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void IndexRegressions()
    {
        var index = new SessionApplications();
        var population = Frame(0, Enumerable.Range(0, 1300).Select(i => ApplicationRow(i)).Append(new Row(null, 7, 2)).ToArray());
        index.Observe(population);
        var all = index.Snapshot();
        Check(all.Length == 1301, "Session index retains all 1,300 application identities plus unattributed traffic, beyond the old 1,024 limit");
        Check(all.Sum(row => row.Incoming) == 13007 && all.Sum(row => row.Outgoing) == 3902, "Observed application totals conserve every synthetic byte");
        Check(all.All(row => row.Key != "presentation-other"), "Session applications are never folded into the bounded preview overflow group");
        index.Observe(population);
        Check(index.Snapshot().Sum(row => row.Incoming) == 13007, "Duplicate source-session/timestamp frame does not double-count totals");
        index.Observe(Frame(1, new Row(new Identity(9999, Epoch.Ticks, "Same executable, another instance", @"c:\synthetic\APPLICATION-0.EXE"), 5, 6)));
        all = index.Snapshot();
        Check(all.Length == 1301 && all.Single(row => row.Key == @"C:\SYNTHETIC\APPLICATION-0.EXE").Incoming == 15, "Executable paths aggregate case-insensitively across process instances");
        var unresolved = new Identity(55, Epoch.Ticks, "Unresolved fixture", null);
        index.Observe(Frame(2, new Row(unresolved, 8, 4), new Row(null, 1, 1)));
        index.Observe(Frame(3, new Row(unresolved with { StartUtcTicks = Epoch.Ticks + 1 }, 2, 1)));
        index.Observe(Frame(4, new Row(unresolved, 3, 2)) with { SourceSession = "another-synthetic-session" });
        all = index.Snapshot();
        Check(all.Count(row => row.Key.StartsWith("unresolved:", StringComparison.Ordinal)) == 3, "Unresolved identities remain distinct across process creation time and collector session");
        Check(all.Single(row => row.Key == "?") is { Incoming: 8, Outgoing: 3 }, "Unattributed traffic remains a separate explicit entry");
        index.Reset();
        Check(index.Snapshot().Length == 0, "Explicit viewer-session reset clears previous applications");
        Parallel.For(0, 200, second => index.Observe(Frame(second, ApplicationRow(0, 1, 2))));
        Check(index.Snapshot().Single() is { Incoming: 200, Outgoing: 400 }, "Concurrent presentation observations conserve totals under the session index lock");
    }

    static void GridRegressions()
    {
        using var host = new Form { Text = "Cylon Raider synthetic UX fixture", ClientSize = new(980, 620), ShowInTaskbar = false, Opacity = 0 };
        using var table = new SessionApplicationTable { Dock = DockStyle.Fill };
        host.Controls.Add(table);
        Exception? failure = null;
        host.Shown += async (_, _) =>
        {
            try
            {
                var index = new SessionApplications();
                index.Observe(Frame(0, Enumerable.Range(0, 1300).Select(i => ApplicationRow(i)).ToArray()));
                table.UpdateApplications(index.Snapshot());
                await Task.Delay(50);
                var grid = table.Grid;
                Check(grid.VirtualMode && grid.DataSource == null && grid.RowCount == 1300, "Session table exposes every observed application through a virtual grid without data-source rebinding");
                Check(grid.DisplayedRowCount(false) > 5 && grid.RowCount > grid.DisplayedRowCount(false), "Application list has a real scrolling viewport");
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                foreach (DataGridViewColumn column in grid.Columns) column.Width = 320;
                grid.CurrentCell = grid[1, 20];
                grid.FirstDisplayedScrollingRowIndex = 400;
                grid.HorizontalScrollingOffset = 75;
                await Task.Delay(20);
                string? selectedName = grid[0, 20].Value?.ToString();
                string? anchoredName = grid[0, 400].Value?.ToString();
                int first = grid.FirstDisplayedScrollingRowIndex, horizontal = grid.HorizontalScrollingOffset;
                index.Observe(Frame(1, ApplicationRow(20, 900, 4), ApplicationRow(400, 500, 3), ApplicationRow(1300, 7000, 3000)));
                table.UpdateApplications(index.Snapshot().Reverse().ToArray());
                await Task.Delay(30);
                Check(grid.RowCount == 1301, "A newly observed application appends without truncating the session table");
                Check(grid.FirstDisplayedScrollingRowIndex == first && grid[0, first].Value?.ToString() == anchoredName, "A per-second refresh preserves the reader's visible application and vertical scroll");
                Check(grid.HorizontalScrollingOffset == horizontal, "A per-second refresh preserves horizontal scroll");
                Check(grid.CurrentCell is { RowIndex: 20, ColumnIndex: 1 } && grid[0, 20].Value?.ToString() == selectedName, "An offscreen selected application and current column remain selected without pulling the view back to them");
                Check(Convert.ToInt64(grid[1, 20].Value) == 910 && Convert.ToInt64(grid[1, 400].Value) == 510, "Virtual cells display updated totals while application rows remain in place");
                table.UpdateApplications([]);
                Check(grid.RowCount == 0, "A session reset clears presentation rows");
            }
            catch (Exception error) { failure = error; }
            finally { host.Close(); }
        };
        Application.Run(host);
        if (failure != null) throw new InvalidOperationException("Native scrolling fixture failed", failure);
    }

    static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) ?? throw new MissingFieldException(name));
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            if (child is DataGridView or ComboBox or NumericUpDown) continue;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    static void CheckLayout(Control root, string context)
    {
        foreach (Control panel in Descendants(root).Prepend(root).Where(control => control.Visible && control is TableLayoutPanel or FlowLayoutPanel))
        {
            var siblings = panel.Controls.Cast<Control>().Where(control => control.Visible && control.Width > 0 && control.Height > 0).ToArray();
            for (int first = 0; first < siblings.Length; first++)
                for (int second = first + 1; second < siblings.Length; second++)
                    Check(!siblings[first].Bounds.IntersectsWith(siblings[second].Bounds), $"{context}: {siblings[first].GetType().Name} '{siblings[first].Text}' does not overlap '{siblings[second].Text}'");
        }
        foreach (Label label in Descendants(root).OfType<Label>().Where(label => label.Visible && label.Text.Length > 0 && label.AutoSize))
        {
            int required = label.GetPreferredSize(new Size(Math.Max(1, label.Width), 0)).Height;
            Check(label.Height + 2 >= required, $"{context}: label has enough vertical space: {label.Text.Split('\n')[0]}");
            if (label.Parent != null)
                Check(label.Right <= label.Parent.ClientSize.Width - label.Parent.Padding.Right + 3, $"{context}: label stays within its content width: {label.Text.Split('\n')[0]}");
        }
        foreach (Button button in Descendants(root).OfType<Button>().Where(button => button.Visible))
            Check(button.Height >= button.Font.Height + button.Padding.Vertical, $"{context}: button text has vertical room: {button.Text}");
    }

    static void UnifiedShellRegressions()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" || Environment.GetEnvironmentVariable("RAIDER_UX_FIXTURE") != "github-hosted-disposable")
            throw new InvalidOperationException("Unified shell fixture requires explicit RAIDER_UX_FIXTURE=github-hosted-disposable on a disposable GitHub-hosted Windows runner");
        using var existingService = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + NativeService.Name);
        if (PlatformPaths.Installed || NativeService.ProcessId() != null || existingService != null || Directory.Exists(BootConfiguration.Root))
            throw new InvalidOperationException("Unified shell fixture refuses installed mode or any existing boot-service registration/data");
        string fixtureRoot = PlatformPaths.DataRoot;
        if (Directory.Exists(fixtureRoot) || File.Exists(fixtureRoot))
            throw new InvalidOperationException("Unified shell fixture refuses an existing portable profile: " + fixtureRoot);
        if (!fixtureRoot.Contains(Path.Combine("EirenAndI", "CylonRaider-Portable") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected an isolated portable hash profile");
        bool created = false;
        Exception? failure = null;
        try
        {
            new SettingsFile(fixtureRoot).Save(new Preferences { AutoMonitor = false, StartInTray = true, OpenMainAtLaunch = false, HourlyReports = false, ContinueInTray = false });
            created = true;
            using var window = new MainWindow();
            window.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Delay(100);
                    Check(window.Visible && window.ShowInTaskbar, "Explicit application launch opens the unified window despite automatic-launch tray preferences");
                    Check(typeof(MainWindow).GetField("pipe", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) == null, "Unified shell fixture has not started an ETW collector");
                    var nav = Field<ListBox>(window, "navigation");
                    string[] expected = ["Dashboard", "Live Traffic", "Top Applications Observed", "Monitoring", "History", "Measurement Health", "Storage", "Startup Service", "Settings", "About & Maintenance"];
                    Check(nav.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(expected), "Unified navigation has the ten requested sections in the requested order");
                    var sections = Field<List<Panel>>(window, "sections");
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    {
                        window.ClientSize = new(1440, 960);
                        window.Font = new Font("Segoe UI", 10 * scale);
                        for (int section = 0; section < expected.Length; section++)
                        {
                            nav.SelectedIndex = section;
                            await Task.Delay(30);
                            if (section is 3 or 6 or 7 or 8 or 9)
                            {
                                var options = Field<OptionsWindow>(window, "optionsWindow");
                                options.Font = new Font("Segoe UI", 10 * scale);
                                Check(!options.TopLevel && options.Parent == sections[section], $"{scale:P0}: {expected[section]} uses the embedded settings form");
                                Check(!Field<ListBox>(options, "navigation").Visible, $"{scale:P0}: the legacy secondary settings navigation remains hidden");
                            }
                            window.PerformLayout();
                            await Task.Delay(10);
                            Check(sections[section].Visible && sections.Where((_, index) => index != section).All(panel => !panel.Visible), $"{scale:P0}: only the selected {expected[section]} section is visible");
                            Check(TextRenderer.MeasureText(expected[section], nav.Font).Width + 20 <= nav.ClientSize.Width, $"{scale:P0}: {expected[section]} navigation label fits");
                            if (section == 4)
                            {
                                var history = Field<HistoryWindow>(window, "historyWindow");
                                Check(!history.TopLevel && history.Parent?.Name == "HistoryHost", $"{scale:P0}: history is embedded in the unified application window");
                            }
                            Check(Application.OpenForms.Cast<Form>().Where(form => form.TopLevel).SequenceEqual(new[] { window }), $"{scale:P0}: {expected[section]} opens no second application window");
                            CheckLayout(window, $"Font {scale:P0}, {expected[section]}");
                        }
                    }
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    try
                    {
                        var exit = typeof(MainWindow).GetMethod("ExitApp", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("ExitApp");
                        await ((Task)(exit.Invoke(window, null) ?? throw new InvalidOperationException("No exit task")));
                        if (!window.IsDisposed)
                        {
                            failure ??= new InvalidOperationException("Orderly application exit did not close the fixture window");
                            window.Dispose();
                        }
                    }
                    catch (Exception error) { failure ??= error; window.Dispose(); }
                }
            };
            Application.Run(window);
            if (failure != null) throw new InvalidOperationException("Unified shell fixture failed", failure);
        }
        finally
        {
            if (created && Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true);
        }
    }
}
