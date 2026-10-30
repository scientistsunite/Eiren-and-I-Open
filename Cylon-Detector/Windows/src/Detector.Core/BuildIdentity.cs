using System.Reflection;
namespace Detector.Core;
public static class BuildIdentity
{
    public const string Version="0.1.0",IntegrityModel="bounded-event-time-v4.1";
    public static string SourceRevision=>typeof(BuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x=>x.Key=="SourceRevision")?.Value is {Length:40} value?value:"unknown";
}
