using BCC.Core;
using System.Security.Principal;

namespace BCC.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            try
            {
                if (args[0] == "--service") return WindowsAgentService.Run();
                if (CommandAccess.RequiresAdministrator(args) && !IsAdministrator()) throw new UnauthorizedAccessException("This operation requires administrator approval.");
                return AgentCommands.Run(args);
            }
            catch (Exception ex)
            {
                MachineStore.Log("Operation failed: " + ex);
                return 1;
            }
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            MessageBox.Show("Battery Charge Control for Windows v0.1 requires Windows 11.", "Compatibility", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
        return 0;
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
