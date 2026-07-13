using System;
using System.Diagnostics;
using System.IO;

namespace OurPlaneCore;

public static class BundledPythonRuntime
{
    public static string ResolveExecutable()
    {
        string bundled = BundledToolPathResolver.ResolveFile(
            Path.Combine("Tools", "python", "python.exe"),
            [Path.Combine("python", "python.exe")]);
        return bundled.Length > 0 ? bundled : "python";
    }

    public static void ConfigureEnvironment(ProcessStartInfo processStartInfo, string pythonExecutable)
    {
        if (!Path.IsPathRooted(pythonExecutable) || !File.Exists(pythonExecutable))
            return;

        string? pythonHome = Path.GetDirectoryName(pythonExecutable);
        if (string.IsNullOrWhiteSpace(pythonHome))
            return;

        processStartInfo.Environment["PYTHONHOME"] = pythonHome;
        string? toolsRoot = Directory.GetParent(pythonHome)?.FullName;
        string bundledDependencies = string.IsNullOrWhiteSpace(toolsRoot)
            ? ""
            : Path.Combine(toolsRoot, "python_deps");
        if (Directory.Exists(bundledDependencies))
            processStartInfo.Environment["PYTHONPATH"] = bundledDependencies;
        else
            processStartInfo.Environment.Remove("PYTHONPATH");
        processStartInfo.Environment["PYTHONNOUSERSITE"] = "1";
        processStartInfo.Environment["PYTHONUTF8"] = "1";
    }
}
