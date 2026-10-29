namespace BCC.Core;

public sealed partial record DeviceInfo(string Manufacturer, string Model, string BiosVersion, string Architecture)
{
    public static string Identity(DeviceInfo d) => $"{d.Manufacturer}|{d.Model}".ToUpperInvariant();
    public string Brand
    {
        get
        {
            var source = Manufacturer.ToUpperInvariant();
            if (source.Contains("ASUSTEK") || source.Contains("ASUS")) return "ASUS";
            if (source.Contains("LENOVO")) return "Lenovo";
            if (source.Contains("HEWLETT") || source == "HP") return "HP";
            if (source.Contains("DELL")) return "Dell";
            if (source.Contains("MICRO-STAR") || source.Contains("MSI")) return "MSI";
            if (source.Contains("ACER")) return "Acer";
            if (source.Contains("SAMSUNG")) return "Samsung";
            if (source.Contains("MICROSOFT")) return "Microsoft";
            if (source.Contains("LG")) return "LG";
            if (source.Contains("HUAWEI")) return "Huawei";
            if (source.Contains("XIAOMI") || source.Contains("REDMI")) return "Xiaomi";
            if (source.Contains("RAZER")) return "Razer";
            if (source.Contains("GIGABYTE")) return "Gigabyte";
            if (source.Contains("FRAMEWORK")) return "Framework";
            if (source.Contains("DYNABOOK") || source.Contains("TOSHIBA")) return "Dynabook";
            return string.IsNullOrWhiteSpace(Manufacturer) ? "Unknown" : Manufacturer;
        }
    }
}
public sealed partial record BatterySnapshot(bool Present, int? Percent, string Power, string Status)
{
    public static BatterySnapshot Decode(bool success, byte ac, byte flags, byte percent)
    {
        if (!success) return new(false, null, "Unknown", "Windows battery information unavailable");
        bool present = (flags & 0x80) == 0;
        string power = ac switch { 1 => "Plugged in", 0 => "On battery", _ => "Unknown AC state" };
        string status = !present ? "No battery detected" : (flags & 8) != 0 ? "Charging" : "Not reporting charging";
        return new(present, percent <= 100 && present ? percent : null, power, status);
    }
}

public sealed class MachinePolicy
{
    public const int SchemaVersion = 1;
    public int Version { get; set; } = SchemaVersion;
    public bool Enabled { get; set; }
    // Optional, backward-compatible field: restore duty is independent of auto-reapply.
    public bool RestorationRequired { get; set; }
    public bool MaintenancePending { get; set; }
    public int? SelectedLimit { get; set; }
    public int? OriginalLimit { get; set; }
    public string OriginalSource { get; set; } = "";
    public string MachineIdentity { get; set; } = "";
    public string Brand { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTimeOffset LastChangedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum ControlAvailability { Unsupported, Unverified, Available, TemporarilyInaccessible }
public sealed record ControllerProbe(ControlAvailability Availability, string Description, int? CurrentLimit, IReadOnlyList<int> CandidateLimits, bool CandidateLimitsVerified);
public interface IChargingController
{
    string Brand { get; }
    ControllerProbe Probe();
    int? ReadLimit();
    void SetLimit(int percent); // throws if request is rejected or cannot be verified
}
