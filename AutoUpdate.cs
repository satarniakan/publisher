using System.Globalization;

namespace Publisher;

public static class AutoUpdate
{
    public static int PrintStatus(Profile profile)
    {
        if (profile.AutoUpdate)
            Ui.Ok($"آپدیت خودکار «{profile.Name}» روشن است (بررسی هر {profile.AutoUpdateIntervalMinutes} دقیقه).");
        else
            Ui.Warn($"آپدیت خودکار «{profile.Name}» خاموش است.");

        Ui.Info($"اجرای دائمی: publisher watch {profile.Name}   |   یک‌بار: publisher watch {profile.Name} --once");
        return 0;
    }

    public static int SetState(AppConfig config, string configPath, Profile profile, bool enabled)
    {
        profile.AutoUpdate = enabled;
        ConfigStore.Save(config, configPath);

        if (enabled)
        {
            if (profile.AutoUpdateIntervalMinutes < 1) profile.AutoUpdateIntervalMinutes = 5;
            Ui.Ok($"آپدیت خودکار «{profile.Name}» روشن شد (بررسی هر {profile.AutoUpdateIntervalMinutes} دقیقه).");
            Ui.Info($"اجرای دائمی روی سرور: publisher watch {profile.Name}");
            Ui.Info($"اجرای دوره‌ای (cron/systemd timer): publisher watch {profile.Name} --once");
        }
        else
        {
            Ui.Ok($"آپدیت خودکار «{profile.Name}» خاموش شد.");
        }

        return 0;
    }

    public static async Task<int> RunAsync(AppConfig config, string configPath, Profile profile,
        int? intervalOverride, bool once, CancellationToken ct)
    {
        if (!profile.AutoUpdate)
        {
            throw new ToolException(
                $"آپدیت خودکار «{profile.Name}» خاموش است. برای روشن کردن: publisher auto {profile.Name} on");
        }

        Deployer.Validate(profile);

        var minutes = Math.Max(1, intervalOverride ?? (profile.AutoUpdateIntervalMinutes > 0 ? profile.AutoUpdateIntervalMinutes : 5));
        var interval = TimeSpan.FromMinutes(minutes);
        var deployer = new Deployer(config, configPath);

        Ui.Section("آپدیت خودکار" + (once ? " (یک‌بار)" : ""));
        Ui.Info($"پروفایل: {profile.Name}  |  بررسی هر {minutes} دقیقه");
        if (!once) Ui.Info("برای توقف Ctrl+C بزنید.");

        var exit = 0;
        while (!ct.IsCancellationRequested)
        {
            Ui.Raw("");
            Ui.Raw($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] بررسی نسخه جدید...");
            try
            {
                exit = await Deployer.WithLockAsync(profile,
                    () => deployer.DeployAsync(profile, assumeYes: true, force: false, ct, quiet: true)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ToolException ex)
            {
                Ui.Err(ex.Message);
                exit = 1;
            }

            if (once) return exit;

            Ui.Info($"بررسی بعدی تا {minutes} دقیقه دیگر...");
            await Task.Delay(interval, ct).ConfigureAwait(false);
        }

        return 0;
    }
}
