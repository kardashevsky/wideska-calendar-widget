using System.Globalization;
using System.Windows;

namespace Wideska.Services;

public static class AppLocalization
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    private static bool initialized;
    private static bool russian;
    private static readonly Dictionary<string, (string English, string Russian)> Strings = new()
    {
        ["Today"] = ("Show today", "Показать сегодня"),
        ["Language"] = ("Language", "Язык"),
        ["Quarter"] = ("Q{0}", "{0} квартал"),
        ["Normal window"] = ("Normal window", "Обычное окно"),
        ["Always on top"] = ("Always on top", "Поверх окон"),
        ["Behind other windows"] = ("Behind other windows", "Под окнами приложений"),
        ["System language"] = ("System language", "Как в системе"),
        ["Day"] = ("Day", "День"), ["3 days"] = ("3 days", "3 дня"),
        ["Workweek"] = ("Workweek", "Рабочая неделя"), ["Week"] = ("Week", "Неделя"),
        ["Month"] = ("Month", "Месяц"), ["Year"] = ("Year", "Год"),
        ["MON"] = ("MON", "ПН"), ["TUE"] = ("TUE", "ВТ"), ["WED"] = ("WED", "СР"),
        ["THU"] = ("THU", "ЧТ"), ["FRI"] = ("FRI", "ПТ"), ["SAT"] = ("SAT", "СБ"), ["SUN"] = ("SUN", "ВС"),
        ["Previous period"] = ("Previous period", "Предыдущий период"),
        ["Next period"] = ("Next period", "Следующий период"),
        ["Calendar view"] = ("Calendar view", "Вид календаря"),
        ["Done"] = ("Done", "Готово"), ["Make it yours"] = ("Make it yours", "Настройки"),
        ["APPEARANCE"] = ("APPEARANCE", "ВНЕШНИЙ ВИД"),
        ["Light text for dark wallpapers"] = ("Light text for dark wallpapers", "Светлый текст для тёмных обоев"),
        ["Background tint"] = ("Background tint", "Затемнение фона"),
        ["Background tint opacity"] = ("Background tint opacity", "Непрозрачность фона"),
        ["More contrast"] = ("More contrast", "Контрастнее"),
        ["Fully transparent"] = ("Fully transparent", "Полностью прозрачный"),
        ["Only the background changes. Text stays crisp."] = ("Only the background changes. Text stays crisp.", "Меняется только фон. Текст остаётся чётким."),
        ["ON YOUR DESKTOP"] = ("ON YOUR DESKTOP", "НА РАБОЧЕМ СТОЛЕ"),
        ["Attached to the desktop, below application windows."] = ("Attached to the desktop, below application windows.", "На рабочем столе, под окнами приложений."),
        ["Lock position and size"] = ("Lock position and size", "Закрепить положение и размер"),
        ["Start with Windows"] = ("Start with Windows", "Запускать вместе с Windows"),
        ["TIME ZONE"] = ("TIME ZONE", "ЧАСОВОЙ ПОЯС"),
        ["Use the selected calendar time zone"] = ("Use the selected calendar time zone", "Использовать часовой пояс выбранного календаря"),
        ["CALENDARS"] = ("CALENDARS", "КАЛЕНДАРИ"),
        ["Disconnect Google account"] = ("Disconnect Google account", "Отключить аккаунт Google"),
        ["Drag the month heading to move. Drag any edge or corner to resize. Find the widget again in the system tray."] = ("Drag the month heading to move. Drag any edge or corner to resize. Find the widget again in the system tray.", "Перетаскивайте заголовок для перемещения, границы и углы — для изменения размера. Управление виджетом находится в трее."),
        ["Drag to resize"] = ("Drag to resize", "Потяните для изменения размера"),
        ["Back to calendar"] = ("Back to calendar", "Вернуться к календарю"),
        ["DESCRIPTION"] = ("DESCRIPTION", "ОПИСАНИЕ"), ["LOCATION"] = ("LOCATION", "МЕСТО"),
        ["All day"] = ("All day", "Весь день"),
        ["More all-day events"] = ("More all-day events", "Ещё события на весь день"),
        ["Show widget"] = ("Show widget", "Показать виджет"),
        ["Hide widget (keep running)"] = ("Hide widget (keep running)", "Скрыть виджет (не закрывать)"),
        ["Settings / Google sign-in"] = ("Settings / Google sign-in", "Настройки / Вход в Google"),
        ["Sync calendars"] = ("Sync calendars", "Обновить календари"),
        ["Move to visible screen"] = ("Move to visible screen", "Переместить на видимый экран"),
        ["Fill desktop work area"] = ("Fill desktop work area", "На всю область рабочего стола"),
        ["Restore widget size"] = ("Restore widget size", "Восстановить размер виджета"),
        ["Exit Wideska"] = ("Exit Wideska", "Выйти из Wideska"),
        ["Your day.\nPart of your desktop."] = ("Your day.\nPart of your desktop.", "Ваш день.\nНа вашем рабочем столе."),
        ["Connect Google Calendar to bring your schedule onto your wallpaper."] = ("Connect Google Calendar to bring your schedule onto your wallpaper.", "Подключите Google Календарь, чтобы видеть расписание на рабочем столе."),
        ["Import Google OAuth file"] = ("Import Google OAuth file", "Импортировать файл Google OAuth"),
        ["Continue with Google  →"] = ("Continue with Google →", "Продолжить с Google →"),
        ["Read-only access · Your events stay on this PC"] = ("Read-only access · Your events stay on this PC", "Только чтение · События хранятся на этом компьютере"),
        ["First time? Set up Google connection"] = ("First time? Set up Google connection", "Первый запуск? Настройте подключение Google"),
        ["1   Enable Google Calendar API in your Cloud project."] = ("1   Enable Google Calendar API in your Cloud project.", "1   Включите Google Calendar API в своём проекте Google Cloud."),
        ["2   In Google Auth platform, configure consent and add your email as a test user. Create a Desktop app client and download its JSON file."] = ("2   In Google Auth platform, configure consent and add your email as a test user. Create a Desktop app client and download its JSON file.", "2   В Google Auth platform настройте экран согласия, добавьте свой адрес как тестового пользователя. Создайте клиент Desktop app и скачайте JSON."),
        ["3   Import that file above, then continue with Google and allow calendar access in your browser."] = ("3   Import that file above, then continue with Google and allow calendar access in your browser.", "3   Импортируйте файл выше, войдите через Google и разрешите доступ к календарю в браузере."),
        ["Open Google Cloud ↗"] = ("Open Google Cloud ↗", "Открыть Google Cloud ↗"),
        ["Setup guide ↗"] = ("Setup guide ↗", "Инструкция ↗"),
        ["Enter OAuth credentials manually"] = ("Enter OAuth credentials manually", "Ввести данные OAuth вручную"),
        ["Desktop OAuth client ID"] = ("Desktop OAuth client ID", "ID клиента Desktop OAuth"),
        ["Desktop OAuth client secret"] = ("Desktop OAuth client secret", "Секрет клиента Desktop OAuth"),
        ["Desktop OAuth client is ready"] = ("Desktop OAuth client is ready", "Клиент Desktop OAuth настроен"),
        ["Import your Google Desktop OAuth file to get started"] = ("Import your Google Desktop OAuth file to get started", "Для начала импортируйте файл Google Desktop OAuth"),
        ["Google Calendar connected"] = ("Google Calendar connected", "Google Календарь подключён"),
        ["Connect Google Calendar to display your events."] = ("Connect Google Calendar to display your events.", "Подключите Google Календарь, чтобы увидеть события."),
        ["Choose the JSON file for an OAuth Desktop app, not a Web app or service account."] = ("Choose the JSON file for an OAuth Desktop app, not a Web app or service account.", "Выберите JSON для клиента OAuth типа Desktop app. Файлы Web app и сервисных аккаунтов не подходят."),
        ["This file does not contain a valid Google OAuth client ID."] = ("This file does not contain a valid Google OAuth client ID.", "В файле нет корректного ID клиента Google OAuth."),
        ["Enter a Google Desktop OAuth client ID in Settings."] = ("Enter a Google Desktop OAuth client ID in Settings.", "Укажите ID клиента Google Desktop OAuth в настройках."),
        ["Google authorization was declined."] = ("Google authorization was declined.", "Авторизация Google отклонена."),
        ["Google returned no authorization code."] = ("Google returned no authorization code.", "Google не вернул код авторизации."),
        ["The OAuth configuration file is too large. Select the Desktop app JSON downloaded from Google Cloud."] = ("The OAuth configuration file is too large. Select the Desktop app JSON downloaded from Google Cloud.", "Файл OAuth слишком большой. Выберите JSON клиента Desktop app, скачанный из Google Cloud."),
        ["Sign in with Google to display your calendar."] = ("Sign in with Google to display your calendar.", "Войдите через Google, чтобы показать календарь."),
        ["Your Google session has expired. Sign in again to display your calendar."] = ("Your Google session has expired. Sign in again to display your calendar.", "Срок действия сеанса Google истёк. Войдите снова, чтобы показать календарь."),
        ["No account connected"] = ("No account connected", "Аккаунт не подключён"),
        ["Disconnecting Google account…"] = ("Disconnecting Google account…", "Отключение аккаунта Google…"),
        ["Account disconnected. Sign in to show your desktop calendar."] = ("Account disconnected. Sign in to show your desktop calendar.", "Аккаунт отключён. Войдите, чтобы показать календарь."),
        ["OAuth client imported. Continue with Google to connect your calendar."] = ("OAuth client imported. Continue with Google to connect your calendar.", "Клиент OAuth импортирован. Продолжите вход через Google."),
        ["Complete authorization in your browser…"] = ("Complete authorization in your browser…", "Завершите авторизацию в браузере…"),
        ["Synchronizing calendars…"] = ("Synchronizing calendars…", "Подключение календарей…"),
        ["Authorization timed out or was cancelled."] = ("Authorization timed out or was cancelled.", "Время авторизации истекло или она была отменена."),
        ["Calendar request timed out. Try again shortly."] = ("Calendar request timed out. Try again shortly.", "Время запроса календаря истекло. Повторите попытку немного позже."),
        ["Showing cached events."] = ("Showing cached events.", "Показаны сохранённые события."),
        ["Cached events could not be removed. Check file permissions."] = ("Cached events could not be removed. Check file permissions.", "Не удалось удалить сохранённые события. Проверьте права доступа к файлам."),
        ["Import Google Desktop OAuth client"] = ("Import Google Desktop OAuth client", "Импорт клиента Google Desktop OAuth"),
        ["Google OAuth JSON (*.json)|*.json"] = ("Google OAuth JSON (*.json)|*.json", "Файл Google OAuth (*.json)|*.json")
    };

    public static string Key(string text) => "Ui." + new string(text.Where(char.IsLetterOrDigit).ToArray());
    public static string Text(string text) => Strings.TryGetValue(text, out var value) ? russian ? value.Russian : value.English : text;
    public static void EnsureInitialized() { if (!initialized) Apply("system"); }
    public static void Apply(string language)
    {
        russian = language == "ru" || language == "system" && SystemCulture.TwoLetterISOLanguageName == "ru";
        var culture = CultureInfo.GetCultureInfo(russian ? "ru-RU" : language == "system" && SystemCulture.TwoLetterISOLanguageName == "en" ? SystemCulture.Name : "en-US");
        Culture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (Application.Current is { } app)
            foreach (var pair in Strings) app.Resources[Key(pair.Key)] = russian ? pair.Value.Russian : pair.Value.English;
        initialized = true;
    }
}
