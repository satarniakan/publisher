namespace Publisher;

public static class Menu
{
    public static async Task<int> RunAsync(AppConfig config, string configPath, CancellationToken ct)
    {
        while (true)
        {
            Ui.Header($"Publisher {Program.AppVersion} — مدیریت انتشار نسخه");
            Ui.Raw($"کانفیگ: {configPath}");
            Ui.Raw($"پروفایل‌ها: {config.Profiles.Count}");
            Ui.Raw("");
            Ui.Raw("  1) انتشار نسخه جدید");
            Ui.Raw("  2) ساخت پروفایل");
            Ui.Raw("  3) ویرایش پروفایل");
            Ui.Raw("  4) حذف پروفایل");
            Ui.Raw("  5) فهرست بک‌آپ‌ها");
            Ui.Raw("  6) بازگردانی از بک‌آپ");
            Ui.Raw("  7) فهرست پروفایل‌ها");
            Ui.Raw("  8) روشن/خاموش کردن آپدیت خودکار");
            Ui.Raw("  0) خروج");

            var choice = Ui.Ask("انتخاب");
            try
            {
                switch (choice)
                {
                    case "1":
                    {
                        var profile = Profiles.Pick(config, null);
                        await Deployer.WithLockAsync(profile,
                            () => new Deployer(config, configPath).DeployAsync(profile, assumeYes: false, force: false, ct)).ConfigureAwait(false);
                        break;
                    }
                    case "2":
                        Profiles.Create(config, configPath, null);
                        break;
                    case "3":
                        Profiles.Edit(config, configPath, null);
                        break;
                    case "4":
                        Profiles.Remove(config, configPath, null);
                        break;
                    case "5":
                    {
                        var profile = Profiles.Pick(config, null);
                        new Deployer(config, configPath).ListBackups(profile);
                        break;
                    }
                    case "6":
                    {
                        var profile = Profiles.Pick(config, null);
                        await Deployer.WithLockAsync(profile,
                            () => new Deployer(config, configPath).RestoreAsync(profile, assumeYes: false, ct)).ConfigureAwait(false);
                        break;
                    }
                    case "7":
                        Profiles.PrintAll(config, configPath);
                        break;
                    case "8":
                    {
                        var profile = Profiles.Pick(config, null);
                        var enable = !profile.AutoUpdate;
                        var verb = enable ? "روشن" : "خاموش";
                        if (!Ui.Confirm($"آپدیت خودکار برای «{profile.Name}» {verb} شود؟", true))
                        {
                            Ui.Info("تغییری انجام نشد.");
                            break;
                        }

                        AutoUpdate.SetState(config, configPath, profile, enable);
                        break;
                    }
                    case "0":
                        return 0;
                    default:
                        Ui.Warn("گزینه نامعتبر است.");
                        break;
                }
            }
            catch (ToolException ex)
            {
                Ui.Err(ex.Message);
            }
            catch (UserCancelException)
            {
                Ui.Warn("عملیات لغو شد.");
            }

            Ui.Pause();
        }
    }
}
