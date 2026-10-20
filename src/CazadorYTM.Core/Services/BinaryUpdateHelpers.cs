namespace CazadorYTM.Core.Services;

using System;
using System.IO;
using System.Linq;

public static class BinaryUpdateHelpers
{
    public static void SafeReplaceFile(string sourcePath, string targetPath)
    {
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        if (File.Exists(targetPath))
        {
            try
            {
                File.Replace(sourcePath, targetPath, null);
                return;
            }
            catch
            {
                var oldPath = targetPath + ".old";
                if (File.Exists(oldPath))
                {
                    try { File.Delete(oldPath); }
                    catch { }
                }
                try { File.Move(targetPath, oldPath); }
                catch { }
                File.Move(sourcePath, targetPath);
            }
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }
    }

    public static void SafeCopyFile(string sourcePath, string targetPath)
    {
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        var stagedPath = targetPath + ".new";
        try
        {
            File.Copy(sourcePath, stagedPath, overwrite: true);
            SafeReplaceFile(stagedPath, targetPath);
        }
        catch
        {
            if (File.Exists(stagedPath))
            {
                try { File.Delete(stagedPath); } catch { }
            }
            throw;
        }
    }

    public static int CompareVersionStrings(string remote, string local)
    {
        try
        {
            var cleanR = remote.Replace("-", ".").Trim();
            var cleanL = local.Replace("-", ".").Trim();

            var partsR = cleanR.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            var partsL = cleanL.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

            var maxLen = Math.Max(partsR.Length, partsL.Length);
            for (var i = 0; i < maxLen; i++)
            {
                var vr = i < partsR.Length ? partsR[i] : 0;
                var vl = i < partsL.Length ? partsL[i] : 0;
                if (vr > vl) return 1;
                if (vr < vl) return -1;
            }
            return 0;
        }
        catch
        {
            return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase);
        }
    }
}
