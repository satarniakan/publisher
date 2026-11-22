using System.Globalization;
using System.IO.Compression;

namespace Publisher;

public sealed class Deployer(AppConfig config, string configPath)
{
    public async Task<int> DeployAsync(Profile profile, bool assumeYes, bool force, CancellationToken ct, bool quiet = false)
    {
        Validate(profile);

        var work = Paths.Expand(profile.WorkingDir);
        var target = Paths.Expand(profile.TargetDir);
        var targetParent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(targetParent)) throw new ToolException("مسیر محل پابلیش نامعتبر است.");

        var backupDir = Paths.Expand(profile.BackupDir);
        var token = ResolveToken(profile);

        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(targetParent);
        var workParent = Path.GetDirectoryName(work);
        if (!string.IsNullOrEmpty(workParent)) Directory.CreateDirectory(workParent);

        await Git.EnsureAvailableAsync(ct).ConfigureAwait(false);

        Ui.Section("دریافت سورس از گیت");
        var branch = await Git.SyncAsync(profile, token, ct).ConfigureAwait(false);
        var branchChanged = false;
        if (!string.Equals(profile.Branch, branch, StringComparison.Ordinal))
        {
            profile.Branch = branch;
            branchChanged = true;
            Ui.Info($"شاخه فعال مخزن: {branch}");
        }

        var head = await Git.HeadAsync(profile, token, work, ct).ConfigureAwait(false);

        if (string.Equals(head.Sha, profile.LastDeployedCommit, StringComparison.OrdinalIgnoreCase) && !force)
        {
            if (quiet)
            {
                Ui.Info($"بدون تغییر ({DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)}) — نسخه {head.ShortSha} از قبل منتشر شده است.");
            }
            else
            {
                var existing = await Git.HistoryAsync(profile, token, work, profile.LastDeployedCommit, 30, ct).ConfigureAwait(false);
                PrintVersion(profile, head, existing);
                Ui.Ok("همین نسخه قبلاً روی سرور منتشر شده است؛ انتشار لازم نیست.");
                Ui.Info("برای انتشار اجباری از گزینه --force استفاده کنید.");
            }

            if (branchChanged) ConfigStore.Save(config, configPath);
            return 0;
        }

        var history = await Git.HistoryAsync(profile, token, work, profile.LastDeployedCommit, 30, ct).ConfigureAwait(false);
        PrintVersion(profile, head, history);

        if (!assumeYes && !Ui.Confirm("انتشار این نسخه انجام شود؟", true))
        {
            Ui.Warn("انتشار لغو شد.");
            if (branchChanged) ConfigStore.Save(config, configPath);
            return 2;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var staging = UniqueDir(targetParent, "." + Paths.SafeName(profile.Name) + "-staging-" + stamp);
        Directory.CreateDirectory(staging);

        Ui.Section("پابلیش خروجی");
        var command = Substitute(profile.PublishCommand, work, target, staging, profile.Name);
        ProcessResult publish;
        try
        {
            publish = await Shell.RunShellAsync(command, work,
                TimeSpan.FromMinutes(Math.Max(1, profile.PublishTimeoutMinutes)),
                echo: true, display: null, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        if (publish.ExitCode != 0)
        {
            TryDelete(staging);
            throw new ToolException($"پابلیش با خطا تمام شد (کد خروج {publish.ExitCode}). نسخه فعلی روی سرور تغییر نکرد.");
        }

        if (!HasFiles(staging))
        {
            TryDelete(staging);
            throw new ToolException("خروجی پابلیش خالی است. بررسی کنید فرمان پابلیش، فایل‌ها را در مسیر {OUTPUT} می‌سازد.");
        }

        Ui.Ok("پابلیش با موفقیت ساخته شد.");

        string? backup = null;
        if (Directory.Exists(target) && HasFiles(target))
        {
            Ui.Section("بک‌آپ از نسخه فعلی");
            var tag = profile.LastDeployedCommit.Length >= 7 ? profile.LastDeployedCommit[..7] : "current";
            try
            {
                backup = Backups.Create(profile, target, tag);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(staging);
                throw new ToolException("بک‌آپ‌گیری از نسخه فعلی ناموفق بود و انتشار متوقف شد: " + ex.Message);
            }

            Backups.Prune(profile);
            Ui.Ok("بک‌آپ ذخیره شد: " + backup);
        }
        else
        {
            Ui.Info("نسخه فعلی روی سرور وجود ندارد یا خالی است؛ بک‌آپ لازم نیست.");
        }

        Ui.Section("جایگزینی نسخه");
        await SwapAsync(profile, staging, target, work, stamp, ct).ConfigureAwait(false);

        profile.LastDeployedCommit = head.Sha;
        profile.LastDeployedAt = DateTimeOffset.Now;
        profile.LastBackupPath = backup ?? "";
        ConfigStore.Save(config, configPath);

        Ui.Section("نتیجه");
        Ui.Ok($"نسخه {head.ShortSha} روی سرور مستقر شد: {target}");
        if (backup is not null) Ui.Info("بک‌آپ نسخه قبلی: " + backup);
        Ui.Info($"برای بازگردانی: publisher restore {profile.Name}");
        return 0;
    }

    public int ListBackups(Profile profile)
    {
        var list = Backups.List(profile);
        Ui.Section("بک‌آپ‌های " + profile.Name);
        if (list.Count == 0)
        {
            Ui.Warn("بک‌آپی پیدا نشد. مسیر: " + Paths.Expand(profile.BackupDir));
            return 0;
        }

        var index = 1;
        foreach (var item in list)
            Ui.Raw($"  {index++,2}) {item.FileName}  |  {FmtDate(new DateTimeOffset(item.Created))}  |  {HumanSize(item.SizeBytes)}  |  کامیت {item.CommitTag}");

        return 0;
    }

    public async Task<int> RestoreAsync(Profile profile, bool assumeYes, CancellationToken ct)
    {
        Validate(profile);

        var backups = Backups.List(profile);
        if (backups.Count == 0)
        {
            Ui.Warn("هیچ بک‌آپی برای این پروفایل وجود ندارد.");
            return 1;
        }

        var pick = Console.IsInputRedirected
            ? backups[0]
            : Ui.Pick("انتخاب بک‌آپ برای بازگردانی", backups,
                b => $"{b.FileName}  —  {FmtDate(new DateTimeOffset(b.Created))}  —  {HumanSize(b.SizeBytes)}");

        Ui.Info("بک‌آپ انتخاب‌شده: " + pick.Path);
        if (!assumeYes && !Ui.Confirm("فایل‌های فعلی با این بک‌آپ جایگزین شوند؟ (از وضعیت فعلی هم بک‌آپ گرفته می‌شود)", false))
        {
            Ui.Warn("بازگردانی لغو شد.");
            return 2;
        }

        var target = Paths.Expand(profile.TargetDir);
        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent)) throw new ToolException("مسیر محل پابلیش نامعتبر است.");
        Directory.CreateDirectory(parent);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var staging = UniqueDir(parent, "." + Paths.SafeName(profile.Name) + "-restore-" + stamp);
        try
        {
            ZipFile.ExtractToDirectory(pick.Path, staging);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TryDelete(staging);
            throw new ToolException("باز کردن فایل بک‌آپ ناموفق بود: " + ex.Message);
        }

        if (!HasFiles(staging))
        {
            TryDelete(staging);
            throw new ToolException("فایل بک‌آپ خالی است.");
        }

        var work = Paths.Expand(profile.WorkingDir);
        string? preBackup = null;
        if (Directory.Exists(target) && HasFiles(target))
        {
            preBackup = Backups.Create(profile, target, "pre-restore");
            Backups.Prune(profile);
            Ui.Info("از وضعیت فعلی بک‌آپ گرفته شد: " + preBackup);
        }

        await SwapAsync(profile, staging, target, work, stamp, ct).ConfigureAwait(false);

        if (IsHex(pick.CommitTag, 7))
        {
            profile.LastDeployedCommit = pick.CommitTag;
            profile.LastDeployedAt = DateTimeOffset.Now;
        }

        if (preBackup is not null) profile.LastBackupPath = preBackup;
        ConfigStore.Save(config, configPath);

        Ui.Ok("بازگردانی با موفقیت انجام شد: " + target);
        Ui.Info("در انتشار بعدی، تغییرات نسبت به کامیت بازگردانده‌شده محاسبه می‌شود.");
        return 0;
    }

    public static void Validate(Profile profile)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name)) missing.Add("نام پروفایل");
        if (string.IsNullOrWhiteSpace(profile.GitUrl)) missing.Add("آدرس مخزن گیت");
        if (string.IsNullOrWhiteSpace(profile.WorkingDir)) missing.Add("دایرکتوری کاری سورس");
        if (string.IsNullOrWhiteSpace(profile.BackupDir)) missing.Add("محل بک‌آپ");
        if (string.IsNullOrWhiteSpace(profile.TargetDir)) missing.Add("محل پابلیش");
        if (string.IsNullOrWhiteSpace(profile.PublishCommand)) missing.Add("فرمان پابلیش");
        if (missing.Count > 0) throw new ToolException("این تنظیمات کامل نیستند: " + string.Join("، ", missing));

        if (profile.Auth is not ("https" or "ssh" or "none"))
            throw new ToolException("روش احراز هویت نامعتبر است (https، ssh یا none).");

        if (profile.AutoUpdate && profile.AutoUpdateIntervalMinutes < 1)
            throw new ToolException("فاصله بررسی آپدیت خودکار باید حداقل 1 دقیقه باشد.");

        if (!profile.PublishCommand.Contains("{OUTPUT}", StringComparison.Ordinal))
            throw new ToolException("فرمان پابلیش باید شامل {OUTPUT} باشد (مسیر خروجی موقت پابلیش).");

        var work = Paths.Expand(profile.WorkingDir);
        var backup = Paths.Expand(profile.BackupDir);
        var target = Paths.Expand(profile.TargetDir);

        if (Paths.IsRoot(target))
            throw new ToolException("محل پابلیش نمی‌تواند ریشه درایو یا فایل‌سیستم باشد.");
        if (Paths.Overlaps(target, work))
            throw new ToolException("محل پابلیش و دایرکتوری سورس نباید یکی یا داخل هم باشند.");
        if (Paths.Overlaps(target, backup))
            throw new ToolException("محل پابلیش و محل بک‌آپ نباید یکی یا داخل هم باشند.");
        if (Paths.Overlaps(backup, work))
            throw new ToolException("محل بک‌آپ و دایرکتوری سورس نباید یکی یا داخل هم باشند.");
    }

    public static async Task<int> WithLockAsync(Profile profile, Func<Task<int>> action)
    {
        using var guard = DeployLock.TryAcquire(profile);
        if (guard is null)
        {
            Ui.Warn("عملیات دیگری برای این پروفایل در حال اجراست؛ لطفاً بعداً تلاش کنید.");
            return 3;
        }

        return await action().ConfigureAwait(false);
    }

    internal static string? ResolveToken(Profile profile)
    {
        if (profile.Auth != "https") return null;
        if (!string.IsNullOrWhiteSpace(profile.Token)) return profile.Token;

        var env = Environment.GetEnvironmentVariable("PUBLISHER_GIT_TOKEN");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        if (Console.IsInputRedirected)
        {
            throw new ToolException(
                "توکن گیت تنظیم نشده است. آن را در کانفیگ ذخیره کنید یا متغیر محیطی PUBLISHER_GIT_TOKEN را مقدار بدهید.");
        }

        Ui.Warn("توکن گیت تنظیم نشده است.");
        var token = Ui.ReadSecret("توکن گیت را وارد کنید");
        if (string.IsNullOrWhiteSpace(token)) throw new ToolException("توکن گیت الزامی است.");
        return token.Trim();
    }

    public static string FmtDate(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var pc = new PersianCalendar();
        return $"{pc.GetYear(local.DateTime):0000}/{pc.GetMonth(local.DateTime):00}/{pc.GetDayOfMonth(local.DateTime):00} {local.DateTime:HH:mm}";
    }

    public static string HumanSize(long bytes)
    {
        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static void PrintVersion(Profile profile, CommitInfo head, IReadOnlyList<CommitInfo> history)
    {
        Ui.Section("اطلاعات نسخه");
        if (profile.LastDeployedCommit.Length > 0)
        {
            var shortSha = profile.LastDeployedCommit.Length > 7 ? profile.LastDeployedCommit[..7] : profile.LastDeployedCommit;
            var when = profile.LastDeployedAt is { } time ? " — " + FmtDate(time) : "";
            Ui.Raw($"نسخه ثبت‌شده روی سرور: {shortSha}{when}");
        }
        else
        {
            Ui.Raw("نسخه ثبت‌شده روی سرور: ندارد (اولین انتشار)");
        }

        Ui.Raw("");
        Ui.Raw($"نسخه جدید: {head.ShortSha} — {head.Subject}");
        Ui.Raw($"  نویسنده: {head.AuthorName} <{head.AuthorEmail}>");
        Ui.Raw($"  تاریخ: {FmtDate(head.Date)}");
        if (head.Body.Length > 0)
        {
            Ui.Raw("  توضیحات:");
            foreach (var line in head.Body.Split('\n'))
                Ui.Raw("    " + line.TrimEnd('\r'));
        }

        Ui.Raw("");
        if (profile.LastDeployedCommit.Length > 0)
        {
            if (history.Count == 0)
            {
                Ui.Raw("تغییری نسبت به نسخه ثبت‌شده وجود ندارد.");
            }
            else
            {
                Ui.Raw($"تغییرات از نسخه ثبت‌شده تا این نسخه ({history.Count} کامیت):");
                foreach (var commit in history.Take(30))
                    Ui.Raw($"  • {commit.ShortSha}  {commit.Subject}  — {commit.AuthorName}  ({FmtDate(commit.Date)})");
            }
        }
        else if (history.Count > 0)
        {
            Ui.Raw($"آخرین تغییرات مخزن ({history.Count} کامیت اخیر):");
            foreach (var commit in history.Take(10))
                Ui.Raw($"  • {commit.ShortSha}  {commit.Subject}  — {commit.AuthorName}  ({FmtDate(commit.Date)})");
        }
    }

    private async Task SwapAsync(Profile profile, string staging, string target, string work, string stamp, CancellationToken ct)
    {
        var oldDir = target + ".old-" + stamp;
        var existed = Directory.Exists(target);
        try
        {
            await RunHookAsync(profile.BeforeReplaceCommand, "قبل از جایگزینی", profile, work, target, staging, ct).ConfigureAwait(false);

            if (existed) Directory.Move(target, oldDir);
            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (existed && Directory.Exists(oldDir) && !Directory.Exists(target)) Directory.Move(oldDir, target);
                throw;
            }

            try
            {
                await RunHookAsync(profile.AfterReplaceCommand, "بعد از جایگزینی", profile, work, target, staging, ct).ConfigureAwait(false);
            }
            catch
            {
                if (existed)
                {
                    Ui.Warn("دستور بعد از جایگزینی ناموفق بود؛ بازگردانی نسخه قبلی انجام می‌شود...");
                    Rollback(target, oldDir, stamp);
                }
                else
                {
                    Ui.Warn("دستور بعد از جایگزینی ناموفق بود؛ نسخه جدید در مقصد باقی ماند (نسخه قبلی برای بازگردانی وجود نداشت).");
                }

                throw;
            }

            if (existed) TryDelete(oldDir);
            Ui.Ok("نسخه جدید جایگزین شد.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(staging);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(staging);
            if (ex is ToolException) throw;
            throw new ToolException("جایگزینی نسخه ناموفق بود: " + ex.Message
                + "\nاگر سرویس در حال اجراست، با دستور «قبل از جایگزینی» آن را متوقف کنید.");
        }
    }

    private static void Rollback(string target, string oldDir, string stamp)
    {
        if (!Directory.Exists(oldDir))
        {
            Ui.Err("پوشه نسخه قبلی پیدا نشد: " + oldDir);
            return;
        }

        if (!TryDelete(target))
        {
            var failed = target + ".failed-" + stamp;
            try
            {
                Directory.Move(target, failed);
                Ui.Warn("نسخه جدید ناموفق نگه داشته شد: " + failed);
            }
            catch (Exception ex)
            {
                Ui.Err("جابه‌جایی نسخه جدید ناموفق بود: " + ex.Message);
            }
        }

        try
        {
            Directory.Move(oldDir, target);
            Ui.Ok("نسخه قبلی بازگردانده شد.");
        }
        catch (Exception ex)
        {
            Ui.Err("بازگردانی خودکار ناموفق بود: " + ex.Message);
            Ui.Err("نسخه قبلی در این مسیر باقی مانده است: " + oldDir);
        }
    }

    private static async Task RunHookAsync(string command, string phase, Profile profile, string work, string target,
        string staging, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var resolved = Substitute(command, work, target, staging, profile.Name);
        var result = await Shell.RunShellAsync(resolved, work, TimeSpan.FromMinutes(10), echo: true, display: null, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new ToolException($"دستور {phase} ناموفق بود (کد خروج {result.ExitCode}).");
    }

    private static string Substitute(string template, string work, string target, string output, string profile)
        => template
            .Replace("{SOURCE}", work, StringComparison.Ordinal)
            .Replace("{TARGET}", target, StringComparison.Ordinal)
            .Replace("{OUTPUT}", output, StringComparison.Ordinal)
            .Replace("{PROFILE}", profile, StringComparison.Ordinal);

    private static bool HasFiles(string dir)
        => Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();

    private static bool IsHex(string value, int length)
        => value.Length == length && value.All(Uri.IsHexDigit);

    internal static bool TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string UniqueDir(string parent, string prefix)
    {
        var path = Path.Combine(parent, prefix);
        var index = 1;
        while (Path.Exists(path)) path = Path.Combine(parent, prefix + "-" + index++);
        return path;
    }
}
