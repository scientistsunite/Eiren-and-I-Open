# Cylon Raider Windows v0.5.1 prerelease

Fixes reported during v0.5 display testing:

- A measured, non-scrolling global header keeps its buttons and monitoring status visible. ON uses a filled green icon, OFF uses a hollow red icon, and waiting or unavailable preview uses an amber icon and explicit text. Color is complemented by words and icon shapes; high-contrast colors are respected.
- The global header offers one state-aware Start/Stop button; the tray retains its monitoring shortcut. Exit remains in About & Maintenance and the tray. The Monitoring section retains Restart and explains collector operation. Start and Pause availability tracks the current connection state.
- Live Traffic filters wrap with their full preferred widths. Process details can be expanded without permanently consuming list space. The chart reserves space below the plot for its scale caption using the current font height.
- Diagnostic retention explains the meaning of 0 and recorded days. Values do not change retention behavior.
- A single window-close event initiates orderly shutdown after the event returns. Restoring the UI during shutdown is ignored. Embedded windows are disposed as children, rather than individually closed as top-level windows. Existing continue-in-tray settings and collector/history/export drain safeguards remain in effect.
- Single left-click on the notification-area icon restores the existing application window; right-click retains the tray menu.

The capture engine, aggregation, history storage, reporting and service implementations are unchanged. This is an unsigned prerelease for Windows x64. Synthetic font-scale/layout and lifecycle tests are distinct from physical 4K display, real ETW, boot service, installer upgrade and multi-monitor acceptance testing.
