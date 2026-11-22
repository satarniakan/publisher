using System.Globalization;
using System.IO.Compression;

namespace Publisher;

public sealed record BackupInfo(string Path, string FileName, DateTime Created, string CommitTag, long SizeBytes);

public static class Backups
{
    public static string Create(Profile profile, string sourceDir, string tag)
    {
        var dir = Paths.Expand(profile.BackupDir);
        Directory.CreateDirectory(dir);

        var baseName = Paths.SafeName(profile.Name)
            + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "_" + Paths.SafeName(tag);

        var final = Path.Combine(dir, baseName + ".zip");
        var index = 1;
        while (File.Exists(final)) final = Path.Combine(dir, baseName + "-" + index++ + ".zip");

        var tmp = final + ".partial";
        try
        {
            ZipFile.CreateFromDirectory(sourceDir, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(tmp, final);
        }
        catch
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            throw;
        }

        return final;
    }

    public static IReadOnlyList<BackupInfo> List(Profile profile)
    {
        var dir = Paths.Expand(profile.BackupDir);
        if (!Directory.Exists(dir)) return Array.Empty<BackupInfo>();

        var prefix = Paths.SafeName(profile.Name) + "_";
        return new DirectoryInfo(dir)
            .EnumerateFiles(prefix + "*.zip")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.Name, f.LastWriteTime, ExtractTag(f.Name), f.Length))
            .ToList();
    }

    public static void Prune(Profile profile)
    {
        if (profile.BackupRetention <= 0) return;

        var list = List(profile);
        foreach (var old in list.Skip(profile.BackupRetention))
        {
            try { File.Delete(old.Path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ExtractTag(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('_');
        return parts.Length >= 3 ? parts[^1] : "";
    }
}
