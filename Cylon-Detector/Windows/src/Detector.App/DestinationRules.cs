using Detector.History;
using Detector.Service;
namespace Detector.App;

// Side-effect-free validation: never creates folders or changes ACLs. Run off the UI thread.
internal sealed record DestinationCheck(string? Logs,string? Reports,string? LogError,string? ReportError)
{
    public bool Valid=>LogError==null&&ReportError==null;
}
internal static class DestinationRules
{
    public static DestinationCheck Check(StartupMode mode,string logs,string reports)
    {
        string? a=null,b=null,ae=null,be=null;
        string Validate(string path){if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))throw new IOException("Enter a complete local folder path.");string full=mode==StartupMode.Boot?BootConfiguration.Destination(path):StoragePaths.LocalDirectory(path);if(File.Exists(full))throw new IOException("Choose a folder, not a file.");return full;}
        try{a=Validate(logs);}catch(Exception ex){ae=ex.Message;}
        try{b=Validate(reports);}catch(Exception ex){be=ex.Message;}
        if(a!=null&&b!=null&&Path.TrimEndingDirectorySeparator(a).Equals(Path.TrimEndingDirectorySeparator(b),StringComparison.OrdinalIgnoreCase))be="Choose a different folder from the detailed logs.";
        return new(a,b,ae,be);
    }
}
