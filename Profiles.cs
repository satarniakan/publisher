namespace Publisher;

public static class Profiles
{
    public static Profile Pick(AppConfig config, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var found = config.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return found ?? throw new ToolException($"پروفایل «{name}» پیدا نشد.");
        }

        if (config.Profiles.Count == 0)
            throw new ToolException("هیچ پروفایلی ساخته نشده است. ابتدا با دستور «publisher init» یکی بسازید.");
        if (config.Profiles.Count == 1) return config.Profiles[0];

        if (Console.IsInputRedirected)
            throw new ToolException("چند پروفایل وجود دارد؛ نام پروفایل را همراه دستور بنویسید: publisher deploy <name>");

        return Ui.Pick("انتخاب پروفایل", config.Profiles, p => $"{p.Name}  →  {p.TargetDir}");
    }

    public static void Create(AppConfig config, string path, string? suggestedName)
    {
        var profile = ProfileWizard.Build(config, existing: null, suggestedName);
        config.Profiles.Add(profile);
        ConfigStore.Save(config, path);
        Ui.Ok($"پروفایل «{profile.Name}» ذخیره شد.");
        Ui.Info("برای انتشار: publisher deploy " + profile.Name);
    }

    public static void Edit(AppConfig config, string path, string? name)
    {
        var current = Pick(config, name);
        var updated = ProfileWizard.Build(config, current, null);
        var index = config.Profiles.IndexOf(current);
        config.Profiles[index] = updated;
        ConfigStore.Save(config, path);
        Ui.Ok($"پروفایل «{updated.Name}» به‌روزرسانی شد.");
    }

    public static void Remove(AppConfig config, string path, string? name)
    {
        var profile = Pick(config, name);
        if (!Ui.Confirm($"پروفایل «{profile.Name}» حذف شود؟ (پوشه‌های سورس، بک‌آپ و پابلیش دست‌نخورده می‌مانند)", false))
        {
            Ui.Info("حذف لغو شد.");
            return;
        }

        config.Profiles.Remove(profile);
        ConfigStore.Save(config, path);
        Ui.Ok("پروفایل حذف شد.");
    }

    public static void PrintAll(AppConfig config, string path)
    {
        Ui.Section("پروفایل‌ها");
        Ui.Raw("کانفیگ: " + path);
        if (config.Profiles.Count == 0)
        {
            Ui.Warn("هنوز پروفایلی ساخته نشده است (publisher init).");
            return;
        }

        var index = 1;
        foreach (var p in config.Profiles)
        {
            Ui.Raw("");
            Ui.Raw($"{index++}) {p.Name}");
            Ui.Raw($"   گیت: {p.GitUrl}  ({(p.Branch.Length > 0 ? p.Branch : "شاخه پیش‌فرض")}, {p.Auth})");
            Ui.Raw($"   مقصد پابلیش: {p.TargetDir}");
            Ui.Raw($"   بک‌آپ: {p.BackupDir}  |  نگهداری: {(p.BackupRetention > 0 ? p.BackupRetention + " فایل" : "بدون محدودیت")}");
            Ui.Raw($"   آپدیت خودکار: {(p.AutoUpdate ? "روشن (هر " + p.AutoUpdateIntervalMinutes + " دقیقه)" : "خاموش")}");
            if (p.LastDeployedCommit.Length > 0)
            {
                var shortSha = p.LastDeployedCommit.Length > 7 ? p.LastDeployedCommit[..7] : p.LastDeployedCommit;
                var when = p.LastDeployedAt is { } time ? " — " + Deployer.FmtDate(time) : "";
                Ui.Raw($"   آخرین انتشار: {shortSha}{when}");
            }
            else
            {
                Ui.Raw("   آخرین انتشار: ندارد");
            }
        }
    }
}
