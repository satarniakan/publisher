namespace Publisher;

public static class ProfileWizard
{
    public static Profile Build(AppConfig config, Profile? existing, string? suggestedName)
    {
        Ui.Section(existing is null ? "ساخت پروفایل جدید" : $"ویرایش پروفایل «{existing.Name}»");

        var profile = Clone(existing);
        profile.Name = AskName(config, existing, suggestedName);

        profile.GitUrl = Ui.AskRequired("آدرس مخزن گیت (HTTPS، SSH یا مسیر محلی)", profile.GitUrl);
        profile.Branch = Ui.Ask("شاخه Git (خالی = شاخه پیش‌فرض مخزن، هنگام اولین اجرا شناسایی می‌شود)", profile.Branch);

        profile.Auth = AskAuth(profile.Auth);
        if (profile.Auth == "https")
        {
            profile.GitUsername = Ui.Ask("نام کاربری گیت (برای GitHub معمولاً x-access-token)",
                string.IsNullOrWhiteSpace(profile.GitUsername) ? "x-access-token" : profile.GitUsername);

            var hasToken = !string.IsNullOrWhiteSpace(profile.Token);
            if (hasToken) Ui.Info("توکن ذخیره‌شده وجود دارد. برای تغییر، توکن جدید وارد کنید.");

            var token = Ui.ReadSecret(hasToken
                ? "توکن جدید (خالی = بدون تغییر)"
                : "توکن گیت (خالی = خواندن از متغیر PUBLISHER_GIT_TOKEN)");

            if (token.Length > 0)
            {
                profile.Token = Ui.Confirm("توکن داخل فایل کانفیگ ذخیره شود؟ (پیشنهاد: خیر و استفاده از متغیر محیطی)", false)
                    ? token
                    : "";
                if (profile.Token.Length == 0)
                    Ui.Info("توکن ذخیره نشد؛ هنگام انتشار باید متغیر PUBLISHER_GIT_TOKEN تنظیم باشد.");
            }
        }
        else if (profile.Auth == "ssh")
        {
            Ui.Info("کلید SSH باید روی سرور برای کاربر جاری تنظیم شده باشد (ssh-agent یا ~/.ssh).");
            profile.GitUsername = "git";
        }

        profile.WorkingDir = Ui.Ask("دایرکتوری کاری دریافت سورس",
            string.IsNullOrWhiteSpace(profile.WorkingDir)
                ? "~/.publisher/work/" + Paths.SafeName(profile.Name)
                : profile.WorkingDir);

        profile.BackupDir = Ui.AskRequired("محل ذخیره بک‌آپ",
            string.IsNullOrWhiteSpace(profile.BackupDir)
                ? "~/.publisher/backups/" + Paths.SafeName(profile.Name)
                : profile.BackupDir);

        profile.TargetDir = Ui.AskRequired("محل پابلیش روی سرور (پوشه‌ای که نسخه جدید جایگزین آن می‌شود)", profile.TargetDir);

        while (true)
        {
            var command = Ui.Ask("فرمان پابلیش (باید خروجی را در {OUTPUT} بسازد)",
                string.IsNullOrWhiteSpace(profile.PublishCommand)
                    ? "dotnet publish -c Release -o \"{OUTPUT}\""
                    : profile.PublishCommand);

            if (command.Contains("{OUTPUT}", StringComparison.Ordinal))
            {
                profile.PublishCommand = command;
                break;
            }

            Ui.Warn("فرمان پابلیش باید شامل {OUTPUT} باشد. مثال: dotnet publish -c Release -o \"{OUTPUT}\"");
        }

        Ui.Info("متغیرهای قابل استفاده: {OUTPUT} مسیر خروجی موقت، {SOURCE} ریشه سورس، {TARGET} محل پابلیش، {PROFILE} نام پروفایل");
        Ui.Info("مثال برای دات‌نت: dotnet publish \"src/MyApp/MyApp.csproj\" -c Release -o \"{OUTPUT}\"");

        profile.BackupRetention = Ui.AskInt("تعداد بک‌آپ‌هایی که نگه داشته شود (0 = بدون محدودیت)",
            profile.BackupRetention > 0 ? profile.BackupRetention : 5, 0, 1000);

        profile.PublishTimeoutMinutes = Ui.AskInt("مهلت پابلیش (دقیقه)",
            profile.PublishTimeoutMinutes > 0 ? profile.PublishTimeoutMinutes : 30, 1, 1440);

        profile.AutoUpdate = Ui.Confirm("آپدیت خودکار روشن باشد؟ (بررسی دوره‌ای و انتشار بدون تأیید)", profile.AutoUpdate);
        if (profile.AutoUpdate)
        {
            profile.AutoUpdateIntervalMinutes = Ui.AskInt("فاصله بررسی خودکار (دقیقه)",
                profile.AutoUpdateIntervalMinutes > 0 ? profile.AutoUpdateIntervalMinutes : 5, 1, 1440);
        }

        Ui.Raw("دستورهای اختیاری (خالی = بدون دستور):");
        profile.BeforeReplaceCommand = Ui.Ask("دستور قبل از جایگزینی (مثلاً توقف سرویس)", profile.BeforeReplaceCommand);
        profile.AfterReplaceCommand = Ui.Ask("دستور بعد از جایگزینی (مثلاً شروع سرویس)", profile.AfterReplaceCommand);

        Deployer.Validate(profile);
        return profile;
    }

    private static string AskName(AppConfig config, Profile? existing, string? suggestedName)
    {
        while (true)
        {
            var name = Ui.AskRequired("نام پروفایل (برای دستورهای بعدی)", existing?.Name ?? suggestedName);
            var duplicate = config.Profiles.Any(p =>
                !ReferenceEquals(p, existing) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (!duplicate) return name;
            Ui.Warn("پروفایلی با این نام وجود دارد؛ نام دیگری انتخاب کنید.");
        }
    }

    private static string AskAuth(string current)
    {
        Ui.Raw("روش دسترسی به گیت:");
        Ui.Raw("  1) توکن HTTPS (Personal Access Token)");
        Ui.Raw("  2) کلید SSH");
        Ui.Raw("  3) مخزن عمومی (بدون احراز هویت)");

        var def = current switch { "ssh" => "2", "none" => "3", _ => "1" };
        while (true)
        {
            var value = Ui.Ask("انتخاب روش", def);
            switch (value)
            {
                case "1": return "https";
                case "2": return "ssh";
                case "3": return "none";
                default: Ui.Warn("عدد 1 تا 3 را وارد کنید."); break;
            }
        }
    }

    private static Profile Clone(Profile? source)
    {
        if (source is null) return new Profile();

        return new Profile
        {
            Name = source.Name,
            GitUrl = source.GitUrl,
            Branch = source.Branch,
            Auth = source.Auth,
            GitUsername = source.GitUsername,
            Token = source.Token,
            WorkingDir = source.WorkingDir,
            BackupDir = source.BackupDir,
            TargetDir = source.TargetDir,
            PublishCommand = source.PublishCommand,
            BackupRetention = source.BackupRetention,
            PublishTimeoutMinutes = source.PublishTimeoutMinutes,
            BeforeReplaceCommand = source.BeforeReplaceCommand,
            AfterReplaceCommand = source.AfterReplaceCommand,
            AutoUpdate = source.AutoUpdate,
            AutoUpdateIntervalMinutes = source.AutoUpdateIntervalMinutes,
            LastDeployedCommit = source.LastDeployedCommit,
            LastDeployedAt = source.LastDeployedAt,
            LastBackupPath = source.LastBackupPath,
        };
    }
}
