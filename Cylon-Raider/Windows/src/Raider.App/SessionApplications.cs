using Raider.Core;

namespace Raider.App;

// Viewer presentation only. Capture, attribution and persisted history retain
// their existing bounded implementations. This index keeps every application
// identity seen during the viewer session, including identities no longer active.
internal sealed class SessionApplications
{
    readonly object gate = new();
    readonly Dictionary<string, Usage> applications = new(StringComparer.Ordinal);
    string? lastSession;
    long lastTicks = long.MinValue;

    public void Reset()
    {
        lock (gate)
        {
            applications.Clear();
            lastSession = null;
            lastTicks = long.MinValue;
        }
    }

    public void Observe(Measurement frame)
    {
        lock (gate)
        {
            if (lastSession == frame.SourceSession && lastTicks == frame.StartUtc.Ticks) return;
            lastSession = frame.SourceSession;
            lastTicks = frame.StartUtc.Ticks;
            foreach (var row in frame.Rows)
            {
                string key = row.Process?.Path?.ToUpperInvariant()
                    ?? (row.Process == null ? "?" : "unresolved:" + (frame.SourceSession ?? "legacy") + ":" + row.Process.Pid + ":" + row.Process.StartUtcTicks);
                var previous = applications.GetValueOrDefault(key)
                    ?? new Usage(key, row.Process?.Name ?? "Unattributed — identity unknown", row.Process?.Path, null, null, 0, 0);
                applications[key] = previous with
                {
                    Incoming = checked(previous.Incoming + row.Incoming),
                    Outgoing = checked(previous.Outgoing + row.Outgoing)
                };
            }
        }
    }

    public Usage[] Snapshot()
    {
        lock (gate) return applications.Values.ToArray();
    }
}
