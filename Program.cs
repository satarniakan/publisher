using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Publisher;

internal static class Program
{
    public const string AppVersion = "1.1.0";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
        }
        catch (IOException) { }
        catch (System.Security.SecurityException) { }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetConsoleCP(65001);
                SetConsoleOutputCP(65001);
            }
            catch (Exception)
            {
                // Not fatal: encoding was already requested through Console.
            }
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ToolException ex)
        {
            Ui.Err(ex.Message);
            return 1;
        }

        var (ascii, autoDetected) = ResolveAsciiMode(options);
        Ui.ConfigureAscii(ascii);
        if (ascii)
        {
            Ui.Info("ASCII mode: Persian text is transliterated to Latin letters.");
            if (autoDetected)
                Ui.Info("This console cannot display Persian. Use a UTF-8 terminal (Windows Terminal, LANG=C.UTF-8) with --no-ascii to keep Persian.");
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested)
            {
                Ui.Warn("در حال پایان عملیات...");
                cts.Cancel();
            }
        };

        try
        {
            return await DispatchAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (UserCancelException)
        {
            Ui.Warn("عملیات لغو شد.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Ui.Warn("عملیات لغو شد.");
            return 2;
        }
        catch (ToolException ex)
        {
            Ui.Err(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Ui.Err("خطای غیرمنتظره: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> DispatchAsync(CliOptions options, CancellationToken ct)
    {
        if (options.ShowVersion)
        {
            Ui.Raw($"publisher {AppVersion}");
            return 0;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowDiagnostics)
        {
            PrintDiagnostics();
            return 0;
        }

        if (options.Command is null && Console.IsInputRedirected)
        {
            PrintHelp();
            return 0;
        }

        var config = ConfigStore.Load(options.ConfigPath);

        switch (options.Command)
        {
            case null:
                return await Menu.RunAsync(config, options.ConfigPath, ct).ConfigureAwait(false);

            case "deploy":
            {
                var profile = Profiles.Pick(config, options.ProfileName);
                var deployer = new Deployer(config, options.ConfigPath);
                return await Deployer.WithLockAsync(profile,
                    () => deployer.DeployAsync(profile, options.Yes, options.Force, ct)).ConfigureAwait(false);
            }

            case "init":
                Profiles.Create(config, options.ConfigPath, options.ProfileName);
                return 0;

            case "edit":
                Profiles.Edit(config, options.ConfigPath, options.ProfileName);
                return 0;

            case "remove":
                Profiles.Remove(config, options.ConfigPath, options.ProfileName);
                return 0;

            case "list":
                Profiles.PrintAll(config, options.ConfigPath);
                return 0;

            case "backups":
            {
                var profile = Profiles.Pick(config, options.ProfileName);
                return new Deployer(config, options.ConfigPath).ListBackups(profile);
            }

            case "restore":
            {
                var profile = Profiles.Pick(config, options.ProfileName);
                var deployer = new Deployer(config, options.ConfigPath);
                return await Deployer.WithLockAsync(profile,
                    () => deployer.RestoreAsync(profile, options.Yes, ct)).ConfigureAwait(false);
            }

            case "auto":
            {
                var name = options.ProfileName;
                var state = options.Value;
                if (state is null && IsOnOff(name))
                {
                    state = name!.ToLowerInvariant();
                    name = null;
                }

                var profile = Profiles.Pick(config, name);
                if (state is null) return AutoUpdate.PrintStatus(profile);
                if (state is not ("on" or "off"))
                    throw new ToolException("وضعیت آپدیت خودکار باید on یا off باشد.");
                return AutoUpdate.SetState(config, options.ConfigPath, profile, state == "on");
            }

            case "watch":
            {
                var profile = Profiles.Pick(config, options.ProfileName);
                return await AutoUpdate.RunAsync(config, options.ConfigPath, profile,
                    options.IntervalMinutes, options.Once, ct).ConfigureAwait(false);
            }

            case "help":
                PrintHelp();
                return 0;

            default:
                Ui.Err($"دستور ناشناخته: {options.Command}");
                PrintHelp();
                return 1;
        }
    }

    private static bool IsOnOff(string? value)
        => !string.IsNullOrEmpty(value)
           && (value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase));

    private static (bool Enabled, bool AutoDetected) ResolveAsciiMode(CliOptions options)
    {
        if (options.AsciiOverride is { } forced) return (forced, false);

        var env = Environment.GetEnvironmentVariable("PUBLISHER_ASCII");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var value = env.Trim().ToLowerInvariant();
            var enabled = value is not ("0" or "false" or "no" or "off");
            return (enabled, false);
        }

        return (ConsoleCannotRenderUnicode(), true);
    }

    private static bool ConsoleCannotRenderUnicode()
        => LegacyConsoleUsesRasterFont() || WindowsLegacyConsole() || !CanRoundtripPersian();

    private static bool WindowsLegacyConsole()
        => OperatingSystem.IsWindows()
           && !Console.IsOutputRedirected
           && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"))
           && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM_PROGRAM"));

    private static bool CanRoundtripPersian()
    {
        try
        {
            var encoding = Console.OutputEncoding;
            const string probe = "پچگژ";
            return string.Equals(encoding.GetString(encoding.GetBytes(probe)), probe, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool LegacyConsoleUsesRasterFont()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected) return false;
        return string.Equals(ConsoleFontName(), "Terminal", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ConsoleFontName()
    {
        try
        {
            var info = new ConsoleFontInfo
            {
                Size = (uint)Marshal.SizeOf<ConsoleFontInfo>(),
                FaceName = string.Empty,
            };
            return GetCurrentConsoleFontEx(GetStdHandle(-11), false, ref info) ? info.FaceName : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void PrintDiagnostics()
    {
        static void P(string key, object? value) => Console.WriteLine(key + ": " + value);

        P("publisher", AppVersion);
        P("os", RuntimeInformation.OSDescription);
        P("arch", RuntimeInformation.ProcessArchitecture + " on " + RuntimeInformation.OSArchitecture);
        P("stdin_redirected", Console.IsInputRedirected);
        P("stdout_redirected", Console.IsOutputRedirected);
        P("stderr_redirected", Console.IsErrorRedirected);
        P("output_encoding", Console.OutputEncoding.WebName + " (cp " + Console.OutputEncoding.CodePage + ")");
        P("input_encoding", Console.InputEncoding.WebName + " (cp " + Console.InputEncoding.CodePage + ")");

        if (OperatingSystem.IsWindows())
        {
            try { P("console_output_cp", GetConsoleOutputCP()); } catch (Exception) { P("console_output_cp", "n/a"); }
            try { P("console_input_cp", GetConsoleCP()); } catch (Exception) { P("console_input_cp", "n/a"); }
            P("console_font", ConsoleFontName() ?? "unknown");
        }

        P("WT_SESSION", Environment.GetEnvironmentVariable("WT_SESSION") ?? "");
        P("LANG", Environment.GetEnvironmentVariable("LANG") ?? "");
        P("LC_ALL", Environment.GetEnvironmentVariable("LC_ALL") ?? "");
        P("TERM", Environment.GetEnvironmentVariable("TERM") ?? "");
        P("PUBLISHER_ASCII", Environment.GetEnvironmentVariable("PUBLISHER_ASCII") ?? "");
        P("persian_roundtrip", CanRoundtripPersian());
        P("ascii_mode", Ui.AsciiMode);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConsoleFontInfo
    {
        public uint Size;
        public uint Font;
        public ushort FontWidth;
        public ushort FontHeight;
        public uint FontFamily;
        public uint FontWeight;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetCurrentConsoleFontEx(IntPtr consoleOutput, bool maximumWindow, ref ConsoleFontInfo info);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCP(uint codePage);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    private static void PrintHelp()
    {
        Ui.Raw("Publisher " + AppVersion + " — مدیریت انتشار نسخه‌ها روی سرور");
        Ui.Raw("");
        Ui.Raw("روش کار: از گیت آخرین سورس دریافت می‌شود، تغییرات نمایش داده می‌شود،");
        Ui.Raw("از پابلیش فعلی بک‌آپ zip گرفته می‌شود، خروجی جدید ساخته و جایگزین می‌شود.");
        Ui.Raw("");
        Ui.Raw("استفاده:");
        Ui.Raw("  publisher                            منوی تعاملی");
        Ui.Raw("  publisher deploy [نام] [--yes] [--force]");
        Ui.Raw("  publisher init [نام]                 ساخت پروفایل جدید");
        Ui.Raw("  publisher edit [نام]                 ویرایش پروفایل");
        Ui.Raw("  publisher remove [نام]               حذف پروفایل");
        Ui.Raw("  publisher list                       فهرست پروفایل‌ها");
        Ui.Raw("  publisher backups [نام]              فهرست بک‌آپ‌ها");
        Ui.Raw("  publisher restore [نام] [--yes]      بازگردانی از بک‌آپ");
        Ui.Raw("  publisher auto [نام] [on|off]        وضعیت یا روشن/خاموش آپدیت خودکار");
        Ui.Raw("  publisher watch [نام] [--once] [--interval دقیقه]");
        Ui.Raw("");
        Ui.Raw("آپدیت خودکار (فقط وقتی برای پروفایل روشن باشد):");
        Ui.Raw("  publisher auto shop on               روشن کردن");
        Ui.Raw("  publisher watch shop                 بررسی دائمی هر 5 دقیقه (یا مقدار پروفایل)");
        Ui.Raw("  publisher watch shop --once          یک بررسی و انتشار، مناسب cron/systemd timer");
        Ui.Raw("  publisher watch shop --interval 10   فاصله بررسی 10 دقیقه برای همین اجرا");
        Ui.Raw("");
        Ui.Raw("گزینه‌ها:");
        Ui.Raw("  --config <مسیر>   مسیر فایل کانفیگ (پیش‌فرض: ~/.publisher/config.json)");
        Ui.Raw("  --yes, -y         تأیید خودکار پرسش‌ها");
        Ui.Raw("  --force           انتشار حتی اگر کامیت جدیدی نباشد");
        Ui.Raw("  --once            در حالت watch فقط یک بررسی انجام بده");
        Ui.Raw("  --interval <n>    فاصله بررسی در watch (دقیقه)");
        Ui.Raw("  --ascii           نمایش متن فارسی با حروف لاتین (کنسول‌های بدون پشتیبانی فارسی)");
        Ui.Raw("  --no-ascii        اجبار خروجی فارسی، حتی اگر کنسول ناتوان تشخیص داده شود");
        Ui.Raw("  --diag            نمایش اطلاعات رمزگذاری و کنسول (همیشه لاتین)");
        Ui.Raw("  -h, --help        این راهنما");
        Ui.Raw("  -v, --version     نسخه برنامه");
        Ui.Raw("");
        Ui.Raw("متغیر محیطی:");
        Ui.Raw("  PUBLISHER_GIT_TOKEN   توکن گیت، وقتی در کانفیگ ذخیره نشده باشد");
        Ui.Raw("  PUBLISHER_ASCII       1=خروجی لاتین، 0=خروجی فارسی همیشگی");
        Ui.Raw("");
        Ui.Raw("مسیر ~ و مسیرهای نسبی پشتیبانی می‌شوند. در فرمان پابلیش، مسیرهای دارای فاصله را در کوتیشن بگذارید.");
    }
}

internal sealed class CliOptions
{
    public string? Command { get; private set; }
    public string? ProfileName { get; private set; }
    public string? Value { get; private set; }
    public string ConfigPath { get; private set; } = ConfigStore.DefaultPath;
    public bool Yes { get; private set; }
    public bool Force { get; private set; }
    public bool Once { get; private set; }
    public int? IntervalMinutes { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }
    public bool ShowDiagnostics { get; private set; }
    public bool? AsciiOverride { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    options.ShowHelp = true;
                    break;
                case "-v" or "--version":
                    options.ShowVersion = true;
                    break;
                case "-y" or "--yes":
                    options.Yes = true;
                    break;
                case "--force":
                    options.Force = true;
                    break;
                case "--once":
                    options.Once = true;
                    break;
                case "--ascii":
                    options.AsciiOverride = true;
                    break;
                case "--no-ascii":
                    options.AsciiOverride = false;
                    break;
                case "--diag":
                    options.ShowDiagnostics = true;
                    break;
                case "--interval":
                    if (i + 1 >= args.Length
                        || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                        || minutes < 1)
                    {
                        throw new ToolException("بعد از --interval عدد دقیقه (حداقل 1) را بنویسید.");
                    }

                    options.IntervalMinutes = minutes;
                    i++;
                    break;
                case "--config":
                    if (i + 1 >= args.Length) throw new ToolException("بعد از --config مسیر فایل را بنویسید.");
                    options.ConfigPath = Paths.Expand(args[++i]);
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ToolException($"گزینه ناشناخته: {arg}");
                    if (options.Command is null)
                        options.Command = arg.ToLowerInvariant();
                    else if (options.ProfileName is null)
                        options.ProfileName = arg;
                    else if (options.Value is null)
                        options.Value = arg.ToLowerInvariant();
                    else
                        throw new ToolException($"آرگومان اضافی: {arg}");
                    break;
            }
        }

        return options;
    }
}
