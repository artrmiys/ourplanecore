using Microsoft.Win32;
using System.IO;

namespace OurPlanCore;

internal static class OurPlanFileAssociationService
{
    private const string ProgId = "OurPlanCore.Project";

    public static void EnsureRegisteredForCurrentExecutable()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.ProcessPath))
            return;

        try
        {
            string executable = Path.GetFullPath(Environment.ProcessPath);
            using RegistryKey classes = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes",
                writable: true);
            using (RegistryKey extension = classes.CreateSubKey(OurPlanPackageFormat.Extension, writable: true))
            {
                extension.SetValue("", ProgId, RegistryValueKind.String);
                extension.SetValue("Content Type", "application/vnd.ourplancore.project", RegistryValueKind.String);
                extension.SetValue("PerceivedType", "document", RegistryValueKind.String);
            }

            using RegistryKey project = classes.CreateSubKey(ProgId, writable: true);
            project.SetValue("", "OurPlanCore Project", RegistryValueKind.String);
            using (RegistryKey icon = project.CreateSubKey("DefaultIcon", writable: true))
                icon.SetValue("", $"\"{executable}\",0", RegistryValueKind.String);
            using (RegistryKey command = project.CreateSubKey(@"shell\open\command", writable: true))
                command.SetValue("", $"\"{executable}\" \"%1\"", RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AppLog.Warn(ex, "Could not register the per-user .ourplan file association.");
        }
    }
}
