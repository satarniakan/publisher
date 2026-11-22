using System.Text;
using System.Text.RegularExpressions;

namespace Publisher;

public static class Text
{
    private static readonly Regex PersianRun = new(
        "[\\u0621-\\u063A\\u0641-\\u064A\\u066E-\\u06D3\\u06FA-\\u06FF\\u200c]+",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Words = BuildWords();
    private static readonly Dictionary<char, string> Chars = BuildChars();

    private static readonly Dictionary<char, string> Extras = new()
    {
        ['—'] = "-",
        ['–'] = "-",
        ['“'] = "\"",
        ['”'] = "\"",
        ['‘'] = "'",
        ['’'] = "'",
        ['…'] = "...",
        ['•'] = "*",
        ['×'] = "x",
        ['→'] = "->",
        ['←'] = "<-",
    };

    public static string ToAscii(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var converted = PersianRun.Replace(value, match => ConvertWord(match.Value));
        var sb = new StringBuilder(converted.Length);
        foreach (var ch in converted)
        {
            if (ch < 0x80)
            {
                sb.Append(ch);
            }
            else if (Chars.TryGetValue(ch, out var mappedChar))
            {
                sb.Append(mappedChar);
            }
            else if (Extras.TryGetValue(ch, out var extra))
            {
                sb.Append(extra);
            }
            else if (ch is >= '\u06F0' and <= '\u06F9')
            {
                sb.Append((char)('0' + (ch - '\u06F0')));
            }
            else if (ch is >= '\u0660' and <= '\u0669')
            {
                sb.Append((char)('0' + (ch - '\u0660')));
            }
            else
            {
                sb.Append('?');
            }
        }

        return sb.ToString();
    }

    private static string ConvertWord(string word)
    {
        if (Words.TryGetValue(word, out var mapped)) return mapped;

        var sb = new StringBuilder(word.Length);
        foreach (var ch in word)
        {
            if (Chars.TryGetValue(ch, out var mappedChar))
            {
                sb.Append(mappedChar);
            }
            else if (ch is >= '\u06F0' and <= '\u06F9')
            {
                sb.Append((char)('0' + (ch - '\u06F0')));
            }
            else if (ch is >= '\u0660' and <= '\u0669')
            {
                sb.Append((char)('0' + (ch - '\u0660')));
            }
        }

        return sb.ToString();
    }

    private static Dictionary<char, string> BuildChars()
    {
        var map = new Dictionary<char, string>();
        void Add(char ch, string value) => map[ch] = value;

        Add('آ', "a"); Add('ا', "a"); Add('أ', "a"); Add('إ', "e"); Add('ب', "b"); Add('پ', "p");
        Add('ت', "t"); Add('ث', "s"); Add('ج', "j"); Add('چ', "ch"); Add('ح', "h"); Add('خ', "kh");
        Add('د', "d"); Add('ذ', "z"); Add('ر', "r"); Add('ز', "z"); Add('ژ', "zh"); Add('س', "s");
        Add('ش', "sh"); Add('ص', "s"); Add('ض', "z"); Add('ط', "t"); Add('ظ', "z"); Add('ع', "a");
        Add('غ', "gh"); Add('ف', "f"); Add('ق', "gh"); Add('ک', "k"); Add('ك', "k"); Add('گ', "g");
        Add('ل', "l"); Add('م', "m"); Add('ن', "n"); Add('و', "o"); Add('ؤ', "o"); Add('ه', "h");
        Add('ة', "h"); Add('ی', "y"); Add('ي', "y"); Add('ئ', "y"); Add('ء', ""); Add('ـ', "");
        Add('\u200c', ""); Add('\u200e', ""); Add('\u200f', "");
        Add('\u202a', ""); Add('\u202b', ""); Add('\u202c', ""); Add('\u202d', ""); Add('\u202e', "");
        Add('،', ","); Add('؛', ";"); Add('؟', "?"); Add('٪', "%"); Add('٫', "."); Add('٬', "");
        Add('«', "\""); Add('»', "\"");
        Add('ً', ""); Add('ٌ', ""); Add('ٍ', ""); Add('َ', ""); Add('ُ', ""); Add('ِ', "");
        Add('ّ', ""); Add('ْ', ""); Add('ٰ', "");

        return map;
    }

    private static Dictionary<string, string> BuildWords()
    {
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string value) => words[key] = value;

        Add("مدیریت", "modiriyat"); Add("انتشار", "enteshar"); Add("نسخه", "noskhe");
        Add("نسخه‌ها", "noskheha"); Add("نسخه‌های", "noskhehaye"); Add("نسخه‌گذاری", "noskhegozari");
        Add("پروفایل", "profile"); Add("پروفایل‌ها", "profileha"); Add("پروفایلی", "profilei");
        Add("بک‌آپ", "backup"); Add("بک‌آپ‌ها", "backupha"); Add("بک‌آپ‌گیری", "backupgiri");
        Add("بازگردانی", "bazgardani"); Add("بازگردانده", "bazgardande"); Add("گیت", "git");
        Add("مخزن", "makhzan"); Add("سورس", "source"); Add("شاخه", "shakhe");
        Add("تنظیمات", "tanzimat"); Add("کانفیگ", "config"); Add("مسیر", "masir");
        Add("مسیرها", "masirha"); Add("محل", "mahal"); Add("پوشه", "folder"); Add("پوشه‌ها", "folderha");
        Add("فایل", "file"); Add("فایل‌ها", "fileha"); Add("فایل‌های", "filehaye");
        Add("دایرکتوری", "directory"); Add("ذخیره", "zakhire"); Add("فرمان", "command");
        Add("دستور", "dastur"); Add("دستورها", "dasturha"); Add("اجرا", "ejra"); Add("اجرای", "ejraye");
        Add("بررسی", "barresi"); Add("تغییرات", "taghirat"); Add("تغییر", "taghir"); Add("جدید", "jadid");
        Add("قبلی", "ghabli"); Add("فعلی", "feli"); Add("آخرین", "akharin"); Add("اولین", "avvalin");
        Add("موفق", "movafagh"); Add("ناموفق", "namovafagh"); Add("خطا", "khata"); Add("هشدار", "hoshdar");
        Add("لغو", "laghv"); Add("تأیید", "taeid"); Add("تایید", "taeid"); Add("ادامه", "edameh");
        Add("شروع", "shorou"); Add("پایان", "payan"); Add("خروج", "khoruj"); Add("انتخاب", "entekhab");
        Add("روشن", "on"); Add("خاموش", "off"); Add("خودکار", "khodkar"); Add("وضعیت", "vaziat");
        Add("اطلاعات", "etelaat"); Add("نتیجه", "natije"); Add("تاریخ", "tarikh");
        Add("نویسنده", "nevisande"); Add("عنوان", "onvan"); Add("توضیحات", "tozihat");
        Add("کامیت", "commit"); Add("کامیت‌ها", "commitha"); Add("زمان", "zaman"); Add("دقیقه", "daghighe");
        Add("روش", "ravesh"); Add("دسترسی", "dastresi"); Add("احراز", "ehraz"); Add("هویت", "hoviyat");
        Add("توکن", "token"); Add("کلید", "key"); Add("عمومی", "omumi"); Add("بدون", "bedoon");
        Add("از", "az"); Add("تا", "ta"); Add("به", "be"); Add("در", "dar"); Add("را", "ra");
        Add("که", "ke"); Add("این", "in"); Add("آن", "an"); Add("برای", "baraye"); Add("هر", "har");
        Add("بیشتر", "bishtar"); Add("کمتر", "kamtar"); Add("تعداد", "tedad"); Add("نگهداری", "negahdari");
        Add("محدودیت", "mahdudiat"); Add("مهلت", "mohlat"); Add("دوره‌ای", "doreyi");
        Add("دائمی", "daemi"); Add("یک‌بار", "yekbar"); Add("مناسب", "monaseb"); Add("سرویس", "service");
        Add("توقف", "tavaghof"); Add("کامل", "kamel"); Add("انجام", "anjam"); Add("نیاز", "niyaz");
        Add("الزامی", "elzami"); Add("نامعتبر", "namotabar"); Add("پیدا", "peyda"); Add("نشد", "nashod");
        Add("وجود", "vojud"); Add("دارد", "darad"); Add("ندارد", "nadarad"); Add("است", "ast");
        Add("نیست", "nist"); Add("شود", "shavad"); Add("می‌شود", "mishavad"); Add("می‌شوند", "mishavand");
        Add("می‌کند", "mikonad"); Add("کنید", "konid"); Add("بزنید", "bezanid"); Add("وارد", "vared");
        Add("عدد", "adad"); Add("شماره", "shomare"); Add("نام", "nam"); Add("آدرس", "address");
        Add("کاربری", "karbari"); Add("ثبت", "sabt"); Add("ثبت‌شده", "sabtshode"); Add("شده", "shode");
        Add("شناسایی", "shenasayi"); Add("فعال", "faal"); Add("پیش‌فرض", "pishfarz");
        Add("انتخاب‌شده", "entekhabshode"); Add("مورد", "mored"); Add("مقدار", "meghdar");
        Add("بعد", "bad"); Add("قبل", "ghabl"); Add("داخل", "dakhel"); Add("یکی", "yeki");
        Add("هم", "ham"); Add("یا", "ya"); Add("و", "va"); Add("فقط", "faghat"); Add("پیام", "payam");
        Add("فهرست", "fehrest"); Add("ساخت", "sakht"); Add("ویرایش", "virayesh"); Add("حذف", "hazf");
        Add("بازگشت", "bazgasht"); Add("منو", "menu"); Add("عملیات", "amaliat"); Add("دیگری", "digari");
        Add("حال", "hal"); Add("لطفا", "lotfan"); Add("بعدا", "baadan"); Add("تلاش", "talash");
        Add("راهنما", "rahnama"); Add("گزینه", "gozine"); Add("متغیر", "motaghayer"); Add("محیط", "mohit");
        Add("محلی", "mahali"); Add("ریشه", "rishe"); Add("تاریخچه", "tarikhche"); Add("اخیر", "akhir");
        Add("مستقر", "mostaghar"); Add("مستقرشده", "mostagharshode"); Add("استقرار", "esteqrar");
        Add("پاک‌سازی", "paksazi"); Add("موقت", "movaghat"); Add("خالی", "khali");
        Add("دست‌نخورده", "dastnakhorde"); Add("مانده", "mandeh"); Add("باقی", "baghi");
        Add("سرور", "server"); Add("برنامه", "barnameh"); Add("کاربر", "karbar"); Add("ساخته", "sakhte");
        Add("ابتدا", "ebteda"); Add("همراه", "hamrah"); Add("بنویسید", "benevisid"); Add("چند", "chand");
        Add("جلوگیری", "jelogiri"); Add("همزمان", "hamzaman"); Add("همزمانی", "hamzamani");
        Add("فعالیت", "faaliyat"); Add("اجراست", "ejrast"); Add("آپدیت", "update"); Add("همین", "hamin");
        Add("متن", "matn"); Add("لاتین", "latin"); Add("فارسی", "farsi"); Add("کنسول", "console");
        Add("ترمینال", "terminal"); Add("باینری", "binary"); Add("خروجی", "khorooji");
        Add("ورودی", "voroodi"); Add("زیپ", "zip"); Add("بازکردن", "bazkardan"); Add("باز", "baz");
        Add("کردن", "kardan"); Add("استفاده", "estefadeh"); Add("مشکل", "moshkel"); Add("نمایش", "namayesh");
        Add("پشتیبانی", "poshtibani"); Add("شوند", "shavand"); Add("نمی‌شود", "nemishavad");
        Add("نمی", "nemi"); Add("نکنید", "nakonid"); Add("باشید", "bashid"); Add("باشد", "bashad");
        Add("هستند", "hastand"); Add("کرد", "kard"); Add("کرده", "karde"); Add("داشته", "dashte");
        Add("بود", "bood"); Add("بوده", "boode"); Add("باید", "bayad"); Add("ممکن", "momken");
        Add("امکان", "emkan"); Add("درست", "dorost"); Add("آماده", "amadeh"); Add("اول", "aval");
        Add("بین", "beyn"); Add("حداقل", "hadaghal"); Add("حداکثر", "hadafsar"); Add("شامل", "shamel");
        Add("مثلاً", "masalan"); Add("مثال", "mesal"); Add("صورت", "soorat"); Add("طریق", "tarigh");
        Add("توسط", "tavasot"); Add("قابل", "ghabele"); Add("قرار", "gharar"); Add("گرفت", "gereft");
        Add("می‌گیرد", "migirad"); Add("می‌دهد", "midahad"); Add("دهد", "dahad"); Add("داده", "dadeh");
        Add("شما", "shoma"); Add("شوید", "shavid"); Add("کنیم", "konim"); Add("هستیم", "hastim");
        Add("هست", "hast"); Add("موجود", "mojud"); Add("هیچ", "hich"); Add("تمام", "tamam");
        Add("همه", "hame"); Add("حالت", "halat"); Add("هنگام", "hengam"); Add("پیش", "pish");
        Add("پس", "pas"); Add("جدا", "joda"); Add("درون", "daroon"); Add("بیرون", "biroun");
        Add("به‌روز", "behrooz"); Add("دوباره", "dobareh"); Add("بار", "bar"); Add("مقصد", "maghsad");
        Add("منبع", "manba"); Add("امن", "amn"); Add("مجوز", "mojvaz"); Add("خرابی", "kharabi");
        Add("دیگر", "digar"); Add("نوع", "noe"); Add("کند", "konad"); Add("شوید", "shavid");
        Add("بخوانید", "bekhanid"); Add("دقیق", "daghigh"); Add("شناسه", "shenase");
        Add("دریافت", "daryaft"); Add("گرفته", "gerefte"); Add("گرفتن", "gereftan");
        Add("منوی", "menu"); Add("وقتی", "vaghti"); Add("نشده", "nashade"); Add("شد", "shod");
        Add("جایگزین", "jaygozin"); Add("روی", "rooy"); Add("پابلیش", "publish");
        Add("میکنند", "mikonand"); Add("میخواهد", "mikhahad"); Add("خواهد", "khahad");
        Add("نسبی", "nesbi"); Add("ناشناخته", "nashenakhte"); Add("آرگومان", "argument");
        Add("اضافی", "ezafi"); Add("غیرمنتظره", "gheyre montazere"); Add("دلیلی", "dalili");
        Add("دلیل", "dalil"); Add("اصلی", "asli"); Add("اشتباه", "eshtebah"); Add("دستی", "dasti");
        Add("انصراف", "enseref"); Add("اطمینان", "etminan"); Add("خطر", "khatar"); Add("مهم", "mohem");
        Add("لطفاً", "lotfan"); Add("پروژه", "project"); Add("کمک", "komak"); Add("خارج", "kharej");
        Add("کردهاید", "kardeid"); Add("ندارد", "nadarad"); Add("شدند", "shodand");
        Add("میشد", "mishod"); Add("نکند", "nakonad"); Add("بگیرید", "begirid");
        Add("دهید", "dehid"); Add("کنند", "konand"); Add("دارند", "darand");
        Add("بیشتر", "bishtar"); Add("کمترین", "kamantarin"); Add("همین", "hamin");
        Add("دیگران", "digaran"); Add("بماند", "bemanad"); Add("مانند", "manand");
        Add("اجبار", "ejbar"); Add("اجباری", "ejbari"); Add("اختیاری", "ekhtiari");
        Add("انتقال", "enteghal"); Add("اگر", "agar"); Add("ب", "b"); Add("با", "ba");
        Add("بازگردانده‌شده", "bazgardandeshode"); Add("باشند", "bashand"); Add("بده", "bede");
        Add("بدهید", "bedehid"); Add("بسازد", "besazad"); Add("بسازید", "besazid");
        Add("بعدی", "badi"); Add("بله", "baleh"); Add("به‌روزرسانی", "behroozresani");
        Add("بک‌آپی", "backupi"); Add("بک‌آپ‌های", "backuphaye"); Add("بک‌آپ‌هایی", "backuphayi");
        Add("بگذارید", "begzarid"); Add("تشخیص", "tashkhis"); Add("تعاملی", "taamoli");
        Add("تغییری", "taghiri"); Add("تنظیم", "tanzim"); Add("جابه‌جایی", "jabejayi");
        Add("جاری", "jari"); Add("جایگزینی", "jaygozini"); Add("جدیدی", "jadidi");
        Add("حتی", "hata"); Add("حروف", "horoof"); Add("خ", "kh"); Add("خطای", "khataye");
        Add("خواندن", "khandan"); Add("خیر", "kheyr"); Add("دات‌نت", "dotnet");
        Add("دارای", "daraye"); Add("درایو", "drive"); Add("دستورهای", "dasturhaye");
        Add("ذخیره‌شده", "zakhire-shode"); Add("عددی", "adadi"); Add("فاصله", "faseleh");
        Add("فایل‌سیستم", "filesystem"); Add("قبلا", "ghablan"); Add("لازم", "lazem");
        Add("ماند", "mand"); Add("متغیرهای", "motaghayerhaye"); Add("متوقف", "motavaghef");
        Add("مثلا", "masalan"); Add("محاسبه", "mohasebeh"); Add("محیطی", "mohiti");
        Add("مسیرهای", "masirhaye"); Add("مشخص", "moshakhas"); Add("معمولا", "mamoolan");
        Add("منتشر", "montasher"); Add("موفقیت", "movafaghiyat"); Add("می‌سازد", "misazad");
        Add("می‌مانند", "mimanand"); Add("ناتوان", "natoan"); Add("نباشد", "nabashad");
        Add("نباید", "nabayad"); Add("نداشت", "nadasht"); Add("نسبت", "nesbat");
        Add("نصب", "nasb"); Add("نمی‌تواند", "nemitavanad"); Add("نکرد", "nakard");
        Add("نگه", "negah"); Add("نیستند", "nistand"); Add("همگام‌سازی", "hamgamsazi");
        Add("همیشگی", "hamishegi"); Add("هنوز", "hanuz"); Add("پاسخ", "pasokh");
        Add("پرسش‌ها", "porseshha"); Add("پوشه‌ای", "pushei"); Add("پوشه‌های", "pushehaye");
        Add("پیشنهاد", "pishnahad"); Add("کار", "kar"); Add("کاری", "kari"); Add("کد", "code");
        Add("کنسول‌های", "consolehaye"); Add("کوتیشن", "quotation"); Add("گزینه‌ها", "gozineha");
        Add("یافت", "yaft"); Add("یک", "yek");

        return words;
    }
}
