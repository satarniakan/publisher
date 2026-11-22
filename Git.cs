using System.Globalization;
using System.Text;

namespace Publisher;

public sealed record CommitInfo(
    string Sha,
    string ShortSha,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset Date,
    string Subject,
    string Body);

public static class Git
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    public static async Task EnsureAvailableAsync(CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await Shell.CaptureAsync("git", ["--version"], null, null, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ToolException("git نصب نیست یا در PATH قرار ندارد. ابتدا Git را روی سرور نصب کنید.");
        }

        if (result.ExitCode != 0) throw new ToolException("git نصب نیست یا در PATH قرار ندارد.");
    }

    private static Dictionary<string, string> Env() => new() { ["GIT_TERMINAL_PROMPT"] = "0" };

    private static List<string> Credentials(Profile profile, string? token)
    {
        var args = new List<string> { "-c", "credential.helper=", "-c", "core.askPass=" };
        if (profile.Auth == "https" && !string.IsNullOrWhiteSpace(token))
        {
            var raw = profile.GitUsername + ":" + token;
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            args.Add("-c");
            args.Add("http.extraHeader=Authorization: Basic " + b64);
        }

        return args;
    }

    private static async Task<ProcessResult> RunAsync(Profile profile, string? token, string? workDir,
        CancellationToken ct, params string[] args)
    {
        var full = Credentials(profile, token);
        full.AddRange(args);
        return await Shell.CaptureAsync("git", full, workDir, Env(), Timeout, ct).ConfigureAwait(false);
    }

    public static async Task<string> SyncAsync(Profile profile, string? token, CancellationToken ct)
    {
        var work = Paths.Expand(profile.WorkingDir);
        var branch = profile.Branch.Trim();

        if (!Path.Exists(Path.Combine(work, ".git")))
        {
            if (Directory.Exists(work) && Directory.EnumerateFileSystemEntries(work).Any())
            {
                throw new ToolException(
                    $"پوشه کاری ({work}) خالی نیست و مخزن گیت هم نیست. آن را خالی کنید یا مسیر دیگری در تنظیمات بدهید.");
            }

            var parent = Path.GetDirectoryName(work);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            var cloneArgs = new List<string> { "clone" };
            if (branch.Length > 0)
            {
                cloneArgs.Add("--branch");
                cloneArgs.Add(branch);
            }

            cloneArgs.Add(profile.GitUrl);
            cloneArgs.Add(work);
            Ui.Command("git clone " + (branch.Length > 0 ? "--branch " + branch + " " : "") + profile.GitUrl);

            var clone = await RunAsync(profile, token, null, ct, cloneArgs.ToArray()).ConfigureAwait(false);
            if (clone.ExitCode != 0) throw new ToolException(GitError("دریافت مخزن ناموفق بود", clone));

            if (branch.Length == 0)
            {
                branch = await DetectDefaultBranchAsync(profile, token, work, ct).ConfigureAwait(false);
                if (branch.Length == 0)
                    throw new ToolException("شاخه پیش‌فرض مخزن شناسایی نشد. شاخه را در تنظیمات پروفایل مشخص کنید.");
            }
        }
        else
        {
            var setUrl = await RunAsync(profile, token, work, ct, "remote", "set-url", "origin", profile.GitUrl).ConfigureAwait(false);
            if (setUrl.ExitCode != 0)
            {
                var addRemote = await RunAsync(profile, token, work, ct, "remote", "add", "origin", profile.GitUrl).ConfigureAwait(false);
                if (addRemote.ExitCode != 0) throw new ToolException(GitError("تنظیم آدرس مخزن ناموفق بود", addRemote));
            }

            Ui.Command("git fetch --prune origin");
            var fetch = await RunAsync(profile, token, work, ct, "fetch", "--prune", "origin").ConfigureAwait(false);
            if (fetch.ExitCode != 0) throw new ToolException(GitError("دریافت تغییرات از گیت ناموفق بود", fetch));

            if (branch.Length == 0)
            {
                branch = await DetectDefaultBranchAsync(profile, token, work, ct).ConfigureAwait(false);
                if (branch.Length == 0)
                    throw new ToolException("شاخه فعلی مخزن شناسایی نشد. شاخه را در تنظیمات پروفایل مشخص کنید.");
            }

            var checkout = await RunAsync(profile, token, work, ct, "checkout", "-f", "-B", branch, "origin/" + branch).ConfigureAwait(false);
            if (checkout.ExitCode != 0) throw new ToolException(GitError($"انتقال به شاخه «{branch}» ناموفق بود", checkout));

            var reset = await RunAsync(profile, token, work, ct, "reset", "--hard", "origin/" + branch).ConfigureAwait(false);
            if (reset.ExitCode != 0) throw new ToolException(GitError("همگام‌سازی سورس با گیت ناموفق بود", reset));

            var clean = await RunAsync(profile, token, work, ct, "clean", "-fd").ConfigureAwait(false);
            if (clean.ExitCode != 0) throw new ToolException(GitError("پاک‌سازی فایل‌های موقت سورس ناموفق بود", clean));
        }

        return branch;
    }

    private static async Task<string> DetectDefaultBranchAsync(Profile profile, string? token, string work, CancellationToken ct)
    {
        var head = await RunAsync(profile, token, work, ct, "symbolic-ref", "--short", "refs/remotes/origin/HEAD").ConfigureAwait(false);
        var name = head.StdOut.Trim();
        if (head.ExitCode == 0 && name.Length > 0)
        {
            const string prefix = "origin/";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
        }

        var current = await RunAsync(profile, token, work, ct, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false);
        var currentName = current.StdOut.Trim();
        return current.ExitCode == 0 && currentName is not ("HEAD" or "") ? currentName : "";
    }

    public static async Task<CommitInfo> HeadAsync(Profile profile, string? token, string work, CancellationToken ct)
    {
        var result = await RunAsync(profile, token, work, ct,
            "log", "-1", "--format=%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%b").ConfigureAwait(false);

        if (result.ExitCode != 0 || result.StdOut.Trim().Length == 0)
            throw new ToolException(GitError("خواندن آخرین کامیت ناموفق بود", result));

        var parts = result.StdOut.TrimEnd('\n', '\r').Split('\u001f');
        if (parts.Length < 6) throw new ToolException("خواندن اطلاعات آخرین کامیت ناموفق بود.");

        DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date);
        return new CommitInfo(parts[0], parts[1], parts[2], parts[3], date, parts[5], parts.Length > 6 ? parts[6].Trim() : "");
    }

    public static async Task<IReadOnlyList<CommitInfo>> HistoryAsync(Profile profile, string? token, string work,
        string fromCommit, int max, CancellationToken ct)
    {
        const string listFormat = "%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e";

        if (!string.IsNullOrWhiteSpace(fromCommit))
        {
            var exists = await RunAsync(profile, token, work, ct, "cat-file", "-e", fromCommit + "^{commit}").ConfigureAwait(false);
            if (exists.ExitCode == 0)
            {
                var range = await RunAsync(profile, token, work, ct,
                    "log", "--no-merges", "-n", max.ToString(CultureInfo.InvariantCulture),
                    fromCommit + "..HEAD", "--format=" + listFormat).ConfigureAwait(false);
                if (range.ExitCode != 0) throw new ToolException(GitError("خواندن تغییرات ناموفق بود", range));
                return ParseList(range.StdOut);
            }
        }

        var recent = await RunAsync(profile, token, work, ct,
            "log", "--no-merges", "-n", Math.Min(max, 10).ToString(CultureInfo.InvariantCulture),
            "--format=" + listFormat).ConfigureAwait(false);
        if (recent.ExitCode != 0) throw new ToolException(GitError("خواندن تاریخچه ناموفق بود", recent));
        return ParseList(recent.StdOut);
    }

    private static List<CommitInfo> ParseList(string output)
    {
        var list = new List<CommitInfo>();
        foreach (var raw in output.Split('\u001e'))
        {
            var record = raw.Trim('\n', '\r', ' ');
            if (record.Length == 0) continue;

            var parts = record.Split('\u001f');
            if (parts.Length < 6) continue;

            DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date);
            list.Add(new CommitInfo(parts[0], parts[1], parts[2], parts[3], date, parts[5], ""));
        }

        return list;
    }

    private static string GitError(string title, ProcessResult result)
    {
        var message = result.StdErr.Trim();
        if (message.Length == 0) message = result.StdOut.Trim();
        return message.Length == 0
            ? $"{title} (کد خروج {result.ExitCode})."
            : $"{title} (کد خروج {result.ExitCode}):{Environment.NewLine}{message}";
    }
}
