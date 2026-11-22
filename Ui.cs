using System.Globalization;
using System.Text;

namespace Publisher;

public static class Ui
{
    private static readonly object Sync = new();
    private static readonly ConsoleColor DefaultColor = Console.ForegroundColor;

    private static void Write(ConsoleColor color, string text)
    {
        lock (Sync)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
        }
    }

    public static void Raw(string text = "") => Write(DefaultColor, text);
    public static void Info(string text) => Write(ConsoleColor.DarkGray, "- " + text);
    public static void Ok(string text) => Write(ConsoleColor.Green, "✓ " + text);
    public static void Warn(string text) => Write(ConsoleColor.Yellow, "! " + text);
    public static void Err(string text) => Write(ConsoleColor.Red, "✗ " + text);
    public static void Command(string text) => Write(ConsoleColor.DarkCyan, "$ " + text);
    public static void Stream(string line, bool isError) => Write(isError ? ConsoleColor.Red : DefaultColor, line);

    public static void Header(string text)
    {
        lock (Sync)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("=============== " + text + " ===============");
            Console.ForegroundColor = old;
        }
    }

    public static void Section(string text)
    {
        lock (Sync)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("-- " + text + " --");
            Console.ForegroundColor = old;
        }
    }

    public static string Ask(string label, string? def = null)
    {
        string? line;
        lock (Sync)
        {
            Console.Write(def is { Length: > 0 } ? $"{label} [{def}]: " : $"{label}: ");
            line = Console.ReadLine();
        }

        if (line is null) throw new UserCancelException("ورودی پایان یافت.");
        line = line.Trim();
        return line.Length == 0 ? def ?? "" : line;
    }

    public static string AskRequired(string label, string? def = null)
    {
        while (true)
        {
            var value = Ask(label, def);
            if (value.Length > 0) return value;
            Warn("این مقدار الزامی است.");
        }
    }

    public static bool Confirm(string question, bool def = false)
    {
        while (true)
        {
            var hint = def ? "بله/خیر [بله]" : "بله/خیر [خیر]";
            var value = Ask($"{question} ({hint})").ToLowerInvariant();
            if (value.Length == 0) return def;
            if (value is "y" or "yes" or "بله" or "ب" or "1") return true;
            if (value is "n" or "no" or "خیر" or "خ" or "0") return false;
            Warn("پاسخ نامعتبر است؛ بله یا خیر بنویسید.");
        }
    }

    public static int AskInt(string label, int def, int min, int max)
    {
        while (true)
        {
            var value = Ask(label, def.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
                return n;
            Warn($"عددی بین {min} و {max} وارد کنید.");
        }
    }

    public static string ReadSecret(string label)
    {
        if (Console.IsInputRedirected) return Ask(label);

        lock (Sync) Console.Write(label + ": ");
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                lock (Sync) Console.WriteLine();
                return sb.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
    }

    public static T Pick<T>(string title, IReadOnlyList<T> items, Func<T, string> format, bool allowCancel = false)
    {
        Section(title);
        for (var i = 0; i < items.Count; i++) Raw($"  {i + 1}) {format(items[i])}");
        if (allowCancel) Raw("  0) انصراف");

        while (true)
        {
            var value = Ask("شماره");
            if (value == "0" && allowCancel) throw new UserCancelException("انصراف");
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= items.Count)
                return items[n - 1];
            Warn("شماره نامعتبر است.");
        }
    }

    public static void Pause(string text = "برای ادامه Enter بزنید")
    {
        if (Console.IsInputRedirected) return;
        lock (Sync) Console.Write(text + " ...");
        Console.ReadLine();
    }
}
