using BCC.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BCC.Controller.Asus;

public sealed partial class AsusController
{
    public AsusController(DeviceInfo device) : this(device, new WindowsAsusTransport()) { }
}

internal sealed class WindowsAsusTransport : IAsusTransport
{
    public int Read() => RunWmi("$r=$x.DSTS([uint32]0x00120057); [Console]::Out.WriteLine([string]$r.result)");
    public int Write(int percent) => RunWmi($"$r=$x.DEVS([uint32]0x00120057,[uint32]{percent.ToString(CultureInfo.InvariantCulture)}); [Console]::Out.WriteLine([string]$r.result)");
    private static int RunWmi(string operation)
    {
        // All scripts/values are generated internally, no user-supplied code or paths are interpolated.
        const string prefix = "$ErrorActionPreference='Stop'; $x=Get-WmiObject -Namespace 'root\\wmi' -Class 'AsusAtkWmi_WMNB' | Select-Object -First 1; if ($null -eq $x) { throw 'ASUS WMI object missing' }; ";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prefix + operation));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not launch");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ASUS firmware query timed out");
        }
        string output = outputTask.GetAwaiter().GetResult().Trim();
        string error = errorTask.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Length > 300 ? error[..300] : error);
        if (!int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new InvalidOperationException("Unrecognized ASUS WMI result: " + (output.Length > 80 ? output[..80] : output));
        return result;
    }
}
