using System.ComponentModel;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Provides a single player-facing language and remembers the choice for this Windows profile.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string Russian = "ru";
    private const string Uzbek = "uz";
    private readonly string _storagePath;
    private string _languageCode;

    public LocalizationService(IConfiguration configuration)
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClubPay", "Agent", "language.txt");
        _languageCode = Normalize(ReadStoredLanguage() ?? configuration["Agent:Language"] ?? Russian);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            var normalized = Normalize(value);
            if (_languageCode == normalized)
                return;

            _languageCode = normalized;
            SaveLanguage(normalized);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public string CultureName => LanguageCode == Uzbek ? "uz-UZ" : "ru-RU";

    public string this[string key]
        => (LanguageCode == Uzbek ? UzbekStrings : RussianStrings).TryGetValue(key, out var value)
            ? value
            : key;

    public string Format(string key, params object[] values)
        => string.Format(CultureInfo.GetCultureInfo(CultureName), this[key], values);

    private string? ReadStoredLanguage()
    {
        try { return File.Exists(_storagePath) ? File.ReadAllText(_storagePath).Trim() : null; }
        catch { return null; }
    }

    private void SaveLanguage(string language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
            File.WriteAllText(_storagePath, language);
        }
        catch
        {
            // Local preference must not stop the kiosk when the profile is read-only.
        }
    }

    private static string Normalize(string? language)
        => string.Equals(language, Uzbek, StringComparison.OrdinalIgnoreCase) ? Uzbek : Russian;

    private static readonly IReadOnlyDictionary<string, string> RussianStrings = new Dictionary<string, string>
    {
        ["GamingClub"] = "ИГРОВОЙ КЛУБ", ["Locked"] = "ЗАБЛОКИРОВАН",
        ["ScanToPay"] = "Сканируйте для оплаты", ["NoInternet"] = "Нет интернета? Сначала подключитесь к Wi‑Fi",
        ["WifiHelp"] = "Отсканируйте QR-код, чтобы подключиться к Wi‑Fi",
        ["Menu"] = "Меню", ["EndSession"] = "Завершить сеанс", ["ActiveSession"] = "АКТИВНЫЙ СЕАНС", ["Remaining"] = "осталось",
        ["ScanToExtendSession"] = "Сканируйте, чтобы продлить сеанс", ["ScanToExtendHelp"] = "Оплатите или примените ваучер",
        ["SessionFrozen"] = "СЕАНС ПРИОСТАНОВЛЕН", ["GracePeriod"] = "ЛЬГОТНОЕ ВРЕМЯ",
        ["ScanToContinuePlaying"] = "Сканируйте, чтобы продолжить игру", ["ScanToContinuePlayingHelp"] = "Оплатите или примените ваучер",
        ["OpenLauncher"] = "Открыть лаунчер", ["OpenApplication"] = "Открыть приложение", ["CloseApplication"] = "Закрыть приложение",
        ["OpenApplications"] = "Открытые приложения", ["GamesAndApps"] = "ИГРЫ И ПРИЛОЖЕНИЯ", ["Running"] = "запущено",
        ["Game"] = "Игра", ["Platform"] = "Платформа",
        ["EndSessionVoucherNotice"] = "Если вы вошли в профиль Clubpay, неиспользованное время сохранится на балансе. Для гостевого сеанса будет создан ваучер.",
        ["PhoneNumber"] = "Номер телефона", ["ConsentVoucherTelegram"] = "Я согласен на хранение номера и получение ваучера в Telegram",
        ["Cancel"] = "Отмена", ["EndAndSend"] = "Завершить сеанс",
        ["PhoneRequired"] = "Укажите номер телефона для отправки ваучера в Telegram.",
        ["ConsentRequired"] = "Нужно согласие на хранение номера и отправку ваучера в Telegram.",
        ["VoucherTitle"] = "Ваучер ClubPay", ["SessionCompleted"] = "Сеанс завершён", ["Done"] = "Готово", ["Voucher"] = "Ваучер",
        ["ProfileTimeSaved"] = "Время сохранено", ["ProfileTimeSavedDescription"] = "{0} добавлено в ваш баланс Clubpay.",
        ["NoTimeRemaining"] = "Время сеанса закончилось. На баланс ничего не нужно сохранять.",
        ["VoucherSent"] = "Ваучер отправлен в Telegram. Проверьте сообщения.", ["GetVoucherInTelegram"] = "Получите ваучер в Telegram",
        ["OpenTelegramBotAndStart"] = "Отсканируйте QR, откройте бота и нажмите Start. После привязки номера ваучер придёт автоматически.",
        ["FindTelegramBot"] = "Или найдите в Telegram",
        ["VoucherBotNotConfigured"] = "Ваучер создан, но Telegram-бот пока не настроен. Сохраните код и обратитесь к администратору.",
        ["VoucherNotDelivered"] = "Ваучер создан. Если он не пришёл в Telegram, обратитесь к администратору.",
        ["EndSessionFailed"] = "Не удалось завершить сеанс: {0}",
        ["LaunchFailed"] = "Не удалось запустить «{0}». Проверьте, что Steam установлен и пользователь вошёл в аккаунт.",
        ["TimeLeft30"] = "До окончания сеанса осталось тридцать минут.", ["TimeLeft10"] = "До окончания сеанса осталось десять минут.",
        ["TimeLeft5"] = "До окончания сеанса осталось пять минут.", ["TimeLeftGeneric"] = "До окончания сеанса осталось {0} мин."
    };

    private static readonly IReadOnlyDictionary<string, string> UzbekStrings = new Dictionary<string, string>
    {
        ["GamingClub"] = "O'YIN KLUBI", ["Locked"] = "BLOKLANGAN",
        ["ScanToPay"] = "To'lov uchun skanerlang", ["NoInternet"] = "Internet yo'qmi? Avval Wi‑Fi'ga ulang",
        ["WifiHelp"] = "Wi‑Fi'ga ulanish uchun QR-kodni skanerlang",
        ["Menu"] = "Menyu", ["EndSession"] = "Seansni yakunlash", ["ActiveSession"] = "FAOL SEANS", ["Remaining"] = "qolgan vaqt",
        ["ScanToExtendSession"] = "Seansni uzaytirish uchun skanerlang", ["ScanToExtendHelp"] = "To'lov qiling yoki vaucher qo'llang",
        ["SessionFrozen"] = "SEANS MUZLATILDI", ["GracePeriod"] = "IMTIYOZ VAQTI",
        ["ScanToContinuePlaying"] = "O'yinni davom ettirish uchun skanerlang", ["ScanToContinuePlayingHelp"] = "To'lov qiling yoki vaucher qo'llang",
        ["OpenLauncher"] = "Launcherni ochish", ["OpenApplication"] = "Ilovani ochish", ["CloseApplication"] = "Ilovani yopish",
        ["OpenApplications"] = "Ochiq ilovalar", ["GamesAndApps"] = "O'YINLAR VA DASTURLAR", ["Running"] = "ishga tushirilgan",
        ["Game"] = "O'yin", ["Platform"] = "Platforma",
        ["EndSessionVoucherNotice"] = "Clubpay profiliga kirgan bo'lsangiz, foydalanilmagan vaqt balansingizda saqlanadi. Mehmon seansi uchun vaucher yaratiladi.",
        ["PhoneNumber"] = "Telefon raqami", ["ConsentVoucherTelegram"] = "Raqamim saqlanishi va vaucher Telegram orqali yuborilishiga roziman",
        ["Cancel"] = "Bekor qilish", ["EndAndSend"] = "Seansni yakunlash",
        ["PhoneRequired"] = "Telegram orqali vaucher yuborish uchun telefon raqamini kiriting.",
        ["ConsentRequired"] = "Raqamni saqlash va vaucher yuborishga rozilik kerak.",
        ["VoucherTitle"] = "ClubPay vaucheri", ["SessionCompleted"] = "Seans yakunlandi", ["Done"] = "Tayyor", ["Voucher"] = "Vaucher",
        ["ProfileTimeSaved"] = "Vaqt saqlandi", ["ProfileTimeSavedDescription"] = "{0} Clubpay balansingizga qo'shildi.",
        ["NoTimeRemaining"] = "Seans vaqti tugadi. Balansga saqlanadigan vaqt yo'q.",
        ["VoucherSent"] = "Vaucher Telegramga yuborildi. Xabarlarni tekshiring.", ["GetVoucherInTelegram"] = "Vaucherni Telegramda oling",
        ["OpenTelegramBotAndStart"] = "QR-kodni skanerlang, botni oching va Start-ni bosing. Raqam bog'langandan keyin vaucher avtomatik keladi.",
        ["FindTelegramBot"] = "Yoki Telegramda toping",
        ["VoucherBotNotConfigured"] = "Vaucher yaratildi, ammo Telegram-bot hali sozlanmagan. Kodni saqlang va administratorga murojaat qiling.",
        ["VoucherNotDelivered"] = "Vaucher yaratildi. Agar u Telegramga kelmasa, administratorga murojaat qiling.",
        ["EndSessionFailed"] = "Seansni yakunlab bo'lmadi: {0}",
        ["LaunchFailed"] = "«{0}» ishga tushmadi. Steam o'rnatilganini va foydalanuvchi akkauntga kirganini tekshiring.",
        ["TimeLeft30"] = "Seans tugashiga o'ttiz daqiqa qoldi.", ["TimeLeft10"] = "Seans tugashiga o'n daqiqa qoldi.",
        ["TimeLeft5"] = "Seans tugashiga besh daqiqa qoldi.", ["TimeLeftGeneric"] = "Seans tugashiga {0} daqiqa qoldi."
    };
}
