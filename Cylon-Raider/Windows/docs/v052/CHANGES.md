# Cylon Raider Windows v0.5.2 prerelease

Includes the merged PR #4 tray-close fix. Changing ShowInTaskbar after hiding the main window recreated its native handle; WinForms then restored maximized/minimized placement and made the window visible again. The application now hides and restores the same native window without changing its taskbar style. Hidden windows already disappear from the taskbar.

With Continue in tray enabled, × hides the application window and monitoring continues according to existing settings. Tray left-click restores the same window. With that preference disabled, × exits through the existing orderly shutdown. Exit Cylon Raider remains available in About & Maintenance and the tray menu.

All v0.5.1 UI features are retained. Capture, measurement, history, storage, report, collector and service implementations are unchanged. Version provenance identifies v0.5.2. The installer retains the existing application ID and preserves data by default.

The Windows recipe runs both the existing 3,916 unified UI regressions and the targeted 53-check native tray-close regression. The latter checks actual native visibility and stable window handles in normal/maximized/minimized states on Dashboard, Monitoring and History, plus tray restoration and maximized-state preservation.

Unsigned Windows x64 prerelease for testing. Hosted synthetic checks do not establish physical M7/4K/DPI, real ETW, boot-service, multi-monitor or installer lifecycle/upgrade acceptance.
