using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Detector.Core;

[SupportedOSPlatform("windows")]
public static class WindowsTimeZone
{
    [StructLayout(LayoutKind.Sequential)]struct SystemTime{public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DynamicZone
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string StandardName;
        public SystemTime StandardDate;public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DaylightName;
        public SystemTime DaylightDate;public int DaylightBias;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
        [MarshalAs(UnmanagedType.U1)]public bool Disabled;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern uint GetDynamicTimeZoneInformation(out DynamicZone zone);
    static string? prior;
    public static void RefreshIfChanged()
    {
        if(GetDynamicTimeZoneInformation(out var zone)==uint.MaxValue)return;
        string value=zone.Key+"|"+zone.Disabled+"|"+zone.Bias;
        if(value==prior)return;prior=value;TimeZoneInfo.ClearCachedData();
    }
}
