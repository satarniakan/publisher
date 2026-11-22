namespace Publisher;

public static class Paths
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Expand(string path)
    {
        var p = path.Trim();
        if (p == "~")
        {
            p = Home;
        }
        else if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
        {
            p = Path.Combine(Home, p[2..]);
        }

        return Path.GetFullPath(p);
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray();
        var result = new string(chars);
        return result.Length == 0 ? "profile" : result;
    }

    public static bool Overlaps(string a, string b)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var ca = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)) + Path.DirectorySeparatorChar;
        var cb = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)) + Path.DirectorySeparatorChar;
        return ca.StartsWith(cb, cmp) || cb.StartsWith(ca, cmp);
    }

    public static bool IsRoot(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? "");
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Length == 0 || string.Equals(full, root, cmp);
    }
}
