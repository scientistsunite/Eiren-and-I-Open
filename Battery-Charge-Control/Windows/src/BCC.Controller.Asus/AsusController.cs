using BCC.Core;

namespace BCC.Controller.Asus;

/// <summary>Independent ASUS WMI controller. The endpoint is documented by public ASUS-tooling research.
/// This module reads 0x00120057 first and refuses writes without readable existing state.
/// It never guesses an original charge limit.</summary>
public sealed partial class AsusController : IChargingController
{
    private readonly DeviceInfo _device;
    public string Brand => "ASUS";
    private static readonly int[] StandardCandidates = [60, 80, 100];
    private static readonly int[] Fa607Candidates = Enumerable.Range(0, 15).Select(i => 30 + i * 5).ToArray();
    private readonly IAsusTransport _transport;
    public AsusController(DeviceInfo device, IAsusTransport transport)
    {
        if (device.Brand != "ASUS") throw new NotSupportedException("Not an ASUS computer");
        _device = device;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public ControllerProbe Probe()
    {
        try
        {
            int? current = ReadLimit();
            if (current is null) return new(ControlAvailability.Unverified,
                "ASUS WMI endpoint was queried, but a recognizable original charging limit could not be read. Writes are blocked to avoid losing the original setting.",
                null, [], false);
            bool fa607 = _device.Model.Contains("FA607", StringComparison.OrdinalIgnoreCase);
            return new(ControlAvailability.Unverified,
                fa607 ? "ASUS FA607 charging endpoint detected. Percentages are candidates, not confirmed supported limits. First write requires consent; firmware readback does not prove physical charge cutoff." :
                "ASUS charging endpoint detected. The listed presets are candidates, not verified for this exact model.",
                current, fa607 ? Fa607Candidates : StandardCandidates, false);
        }
        catch (Exception ex)
        {
            return new(ControlAvailability.TemporarilyInaccessible,
                "ASUS WMI charging interface unavailable: " + ex.Message + ". Check ASUS System Control Interface driver and official ASUS support.",
                null, [], false);
        }
    }

    public int? ReadLimit()
    {
        int raw = _transport.Read();
        // ASUS DSTS traditionally returns 0x10000 + status. Reject other formats rather than guess.
        int candidate = raw >= 65536 && raw <= 65636 ? raw - 65536 : -1;
        return candidate is >= 20 and <= 100 ? candidate : null;
    }

    public void SetLimit(int percent)
    {
        if (percent is < 20 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (!(_device.Model.Contains("FA607", StringComparison.OrdinalIgnoreCase) ? Fa607Candidates : StandardCandidates).Contains(percent))
            throw new NotSupportedException("Requested percentage is not among model-specific candidate limits.");
        if (ReadLimit() is null)
            throw new InvalidOperationException("Current ASUS setting unreadable; hardware write blocked.");
        int result = _transport.Write(percent);
        if (result != 1) throw new InvalidOperationException($"ASUS firmware did not acknowledge requested setting (result {result}).");
        int? readback = ReadLimit();
        if (readback != percent)
            throw new InvalidOperationException($"Command acknowledged but readback did not match {percent}%. Reported: {readback?.ToString() ?? "unavailable"}. Physical charging behavior remains unverified.");
    }

}

public interface IAsusTransport
{
    int Read();
    int Write(int percent);
}
