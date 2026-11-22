using System.Globalization;
using System.Text;

namespace Publisher;

public static class DeployLock
{
    public static FileStream? TryAcquire(Profile profile)
    {
        var work = Paths.Expand(profile.WorkingDir);
        var parent = Path.GetDirectoryName(work);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        try
        {
            var stream = new FileStream(work + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            var info = Encoding.UTF8.GetBytes(
                $"{Environment.ProcessId} {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}");
            stream.Write(info);
            stream.Flush();
            return stream;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
