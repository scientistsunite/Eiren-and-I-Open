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
            DiscardDraftRegressions();
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
    static void DiscardDraftRegressions()
    {
        int saves = 0, actions = 0;
        Exception? failure = null;
        string syntheticRoot = Path.Combine(Path.GetTempPath(), "Raider-Ux-Unsaved-" + Guid.NewGuid().ToString("N"));
        var saved = new Preferences { Startup = StartupMode.Off, DetailedDiagnostics = false, LogDirectory = Path.Combine(syntheticRoot, "logs"), ReportDirectory = Path.Combine(syntheticRoot, "reports") };
        Task Operation() { actions++; return Task.CompletedTask; }
        using var options = new OptionsWindow(saved, _ => { saves++; return Task.CompletedTask; }, () => "Synthetic stopped state", Operation, Operation, Operation, () => actions++, Operation, Operation, Operation);
        options.Shown += async (_, _) =>
        {
            try
            {
                options.SelectSection(3);
                var logs = options.Controls.Find("LogDirectory", true).OfType<TextBox>().Single();
                var reports = options.Controls.Find("ReportDirectory", true).OfType<TextBox>().Single();
                var startup = options.Controls.Find("StartupMode", true).OfType<ComboBox>().Single();
                var details = Field<CheckBox>(options, "details");
                var discard = options.Controls.Find("DiscardChanges", true).OfType<Button>().Single();
                logs.Text = Path.Combine(syntheticRoot, "unsaved-desktop-logs");
                reports.Text = Path.Combine(syntheticRoot, "unsaved-desktop-reports");
                startup.SelectedIndex = (int)StartupMode.Boot;
                logs.Text = BootConfiguration.DefaultLogs + "-unsaved";
                reports.Text = BootConfiguration.DefaultReports + "-unsaved";
                details.Checked = true;
                await Task.Delay(30);
                discard.PerformClick();
                await Task.Delay(30);
                Check(startup.SelectedIndex == (int)saved.Startup && details.Checked == saved.DetailedDiagnostics, "Discard unsaved changes restores saved startup mode and diagnostic capture preference");
                Check(logs.Text == saved.LogDirectory && reports.Text == saved.ReportDirectory, "Discard unsaved changes restores saved detailed-log and report destinations");
                Check(saves == 0 && actions == 0, "Discarding drafts never saves settings, reconfigures capture, or invokes monitoring/service actions");
                startup.SelectedIndex = (int)StartupMode.Boot;
                Check(logs.Text == BootConfiguration.DefaultLogs && reports.Text == BootConfiguration.DefaultReports, "Discard also removes hidden unsaved boot-mode destination drafts");
                startup.SelectedIndex = (int)StartupMode.Off;
                Check(logs.Text == saved.LogDirectory && reports.Text == saved.ReportDirectory, "Switching back after discard retains saved desktop destinations");
                Check(!Directory.Exists(syntheticRoot), "Discard fixture never creates its synthetic destination directories");
            }
            catch (Exception error) { failure = error; }
            finally { options.Close(); }
        };
        Application.Run(options);
        if (failure != null) throw new InvalidOperationException("Discard-draft fixture failed", failure);
    }
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
        static string Geometry(Control control) => $"{control.GetType().Name} '{control.Name}' bounds={control.Bounds}, client={control.ClientSize}, preferred={control.PreferredSize}";
        string windowGeometry = root is Form form ? $"window={form.Bounds}, client={form.ClientSize}, minimum={form.MinimumSize}, workingArea={Screen.FromControl(form).WorkingArea}" : Geometry(root);
        foreach (Control parent in Descendants(root).Prepend(root).Where(control => control.Visible))
        {
            if (parent is ScrollableControl { AutoScroll: true }) continue;
            foreach (Control child in parent.Controls.Cast<Control>().Where(control => control.Visible && control.Width > 0 && control.Height > 0))
            {
                var allowed = parent.ClientRectangle;
                // Native tab borders can use a pixel for their selected page edge.
                allowed.Inflate(1, 1);
                Check(allowed.Contains(child.Bounds), $"{context}: direct child fits parent; {Geometry(child)}; parent {Geometry(parent)}; {windowGeometry}");
            }
        }
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
        foreach (CheckBox checkBox in Descendants(root).OfType<CheckBox>().Where(checkBox => checkBox.Visible))
        {
            var preferred = checkBox.GetPreferredSize(new Size(Math.Max(1, checkBox.ClientSize.Width), 0));
            Check(checkBox.ClientSize.Height + 1 >= preferred.Height, $"{context}: checkbox has its preferred text height; {Geometry(checkBox)}; constrainedPreferred={preferred}; {windowGeometry}");
            if (checkBox.Parent != null)
                Check(checkBox.Right <= checkBox.Parent.ClientSize.Width - checkBox.Parent.Padding.Right + 1 && preferred.Width <= checkBox.Width + 1, $"{context}: checkbox text fits its content width; {Geometry(checkBox)}; constrainedPreferred={preferred}; parent {Geometry(checkBox.Parent)}; {windowGeometry}");
        }
        foreach (Button action in Descendants(root).OfType<Button>().Where(button => button.Visible && button.Name is "DiscardChanges" or "Apply"))
        {
            var full = action.RectangleToScreen(action.ClientRectangle);
            var visible = full;
            for (Control? ancestor = action.Parent; ancestor != null; ancestor = ancestor.Parent)
                visible = Rectangle.Intersect(visible, ancestor.RectangleToScreen(ancestor.ClientRectangle));
            Check(visible == full, $"{context}: footer action remains fully reachable through all ancestor viewports; {Geometry(action)}; visible={visible}, full={full}; {windowGeometry}");
        }
    }

    static void FitFixtureWindow(MainWindow window)
    {
        var fit = typeof(MainWindow).GetMethod("FitWindowToWorkArea", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("FitWindowToWorkArea");
        fit.Invoke(window, null);
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
                    window.ClientSize = new(1000, 650);
                    FitFixtureWindow(window);
                    nav.SelectedIndex = 4;
                    await Task.Delay(80);
                    var compactHistory = Field<HistoryWindow>(window, "historyWindow");
                    var compactApplications = Field<DataGridView>(compactHistory, "applications");
                    Check(compactApplications.Visible && compactApplications.ClientSize.Height >= 80, "At 1000×650, embedded history retains at least 80 pixels of application-list height");
                    Check(compactHistory.Parent != null && compactHistory.Bounds == compactHistory.Parent.ClientRectangle, "At 1000×650, history fills its available panel");
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    {
                        window.Font = new Font("Segoe UI", 10 * scale);
                        var workingArea = Screen.FromControl(window).WorkingArea;
                        int frameWidth = window.Width - window.ClientSize.Width, frameHeight = window.Height - window.ClientSize.Height;
                        window.ClientSize = new(Math.Min(1440, Math.Max(1, workingArea.Width - frameWidth)), Math.Min(960, Math.Max(1, workingArea.Height - frameHeight)));
                        FitFixtureWindow(window);
                        for (int section = 0; section < expected.Length; section++)
                        {
                            nav.SelectedIndex = section;
                            await Task.Delay(30);
                            if (section is 3 or 6 or 7 or 8 or 9)
                            {
                                var options = Field<OptionsWindow>(window, "optionsWindow");
                                options.Font = new Font("Segoe UI", 10 * scale);
                                FitFixtureWindow(window);
                                Check(!options.TopLevel && options.Parent == sections[section], $"{scale:P0}: {expected[section]} uses the embedded settings form");
                                Check(!Field<ListBox>(options, "navigation").Visible, $"{scale:P0}: the legacy secondary settings navigation remains hidden");
                            }
                            window.PerformLayout();
                            await Task.Delay(10);
                            var shellRoot = window.Controls[0];
                            var pageHost = Field<Panel>(window, "pageHost");
                            string dimensions = $"window={window.Bounds}, client={window.ClientSize}, shellRoot={shellRoot.Bounds}/{shellRoot.ClientSize}, pageHost={pageHost.Bounds}/{pageHost.ClientSize}, section={sections[section].Bounds}/{sections[section].ClientSize}, workingArea={Screen.FromControl(window).WorkingArea}";
                            Check(Screen.FromControl(window).WorkingArea.Contains(window.Bounds), $"{scale:P0}: fitted application window remains inside the actual work area; {dimensions}");
                            Check(shellRoot.ClientSize.Width <= window.ClientSize.Width && shellRoot.ClientSize.Height <= window.ClientSize.Height, $"{scale:P0}: shell layout uses the actual fitted client area; {dimensions}");
                            Check(pageHost.ClientRectangle.Contains(sections[section].Bounds), $"{scale:P0}: selected section fits its actual page host; {dimensions}");
                            Check(sections[section].Visible && sections.Where((_, index) => index != section).All(panel => !panel.Visible), $"{scale:P0}: only the selected {expected[section]} section is visible");
                            Check(TextRenderer.MeasureText(expected[section], nav.Font).Width + 20 <= nav.ClientSize.Width, $"{scale:P0}: {expected[section]} navigation label fits");
                            if (section == 4)
                            {
                                var history = Field<HistoryWindow>(window, "historyWindow");
                                Check(!history.TopLevel && history.Parent?.Name == "HistoryHost", $"{scale:P0}: history is embedded in the unified application window");
                            }
                            Check(Application.OpenForms.Cast<Form>().Where(form => form.TopLevel).SequenceEqual(new[] { window }), $"{scale:P0}: {expected[section]} opens no second application window");
                            CheckLayout(window, $"Font {scale:P0}, {expected[section]}");
                            if (scale is 1f or 2f)
                            {
                                // Render only this synthetic fixture's controls. Never
                                // capture the desktop, other windows, or a real profile.
                                string previewRoot = Path.Combine(Environment.CurrentDirectory, "ui-previews");
                                Directory.CreateDirectory(previewRoot);
                                string sectionName = expected[section].Replace(" & ", "-").Replace(' ', '-').ToLowerInvariant();
                                string destination = Path.Combine(previewRoot, $"font-{(int)(scale * 100)}-{section:D2}-{sectionName}.png");
                                using var preview = new Bitmap(window.Width, window.Height);
                                window.DrawToBitmap(preview, new Rectangle(Point.Empty, window.Size));
                                preview.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
                                Console.WriteLine("SYNTHETIC APP PREVIEW " + Path.GetFileName(destination));
                            }
                        }
                    }
                    var area = Screen.FromControl(window).WorkingArea;
                    window.MinimumSize = new(area.Width + 100, area.Height + 100);
                    window.Location = new(area.Left - 100, area.Top - 100);
                    FitFixtureWindow(window);
                    await Task.Delay(30);
                    Check(window.MinimumSize.Width <= area.Width && window.MinimumSize.Height <= area.Height, "After 200% font simulation, the window minimum fits the actual Windows work area");
                    Check(area.Contains(window.Bounds), "After 200% font simulation, work-area fitting keeps all application window bounds on screen");
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
