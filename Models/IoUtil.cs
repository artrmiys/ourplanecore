using System;
using System.IO;

namespace OurPlaneCore;

public static class IoUtil
{
    public static void WriteAllTextAtomic(string path, string contents)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, contents);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    File.Copy(tempPath, path, overwrite: true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            throw;
        }
    }
}
