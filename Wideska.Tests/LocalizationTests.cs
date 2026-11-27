using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestLocalizationAsync(Application app, Window window, string directory)
    {
        var vm = (MainViewModel)window.DataContext;
        string originalLanguage = vm.Language;
        try
        {
            vm.Language = "ru";
            vm.View = CalendarView.SingleDay;
            vm.TodayCommand.Execute(null);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var todayButton = (Button)window.FindName("TodayButton");
            Check((string)todayButton.Content == "Показать сегодня" && vm.Views.Single(v => v.Value == CalendarView.Week).Title == "Неделя",
                "Russian selection must update live resources and calendar view names");
            Check(!vm.ShouldShowToday && todayButton.Visibility == Visibility.Collapsed,
                "Show today must be hidden while the displayed day is today");
            vm.NextCommand.Execute(null);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(vm.ShouldShowToday && todayButton.Visibility == Visibility.Visible,
                "Show today must appear when navigating away from today");
            var header = (FrameworkElement)window.FindName("CalendarHeader");
            var heading = (FrameworkElement)window.FindName("HeaderPeriod");
            Check(Math.Abs(heading.TranslatePoint(new Point(), header).X) < .5,
                "The period heading must stay aligned to the left edge independently of the navigation controls");
            var previous = (Button)window.FindName("PreviousPeriodButton");
            Check(Math.Abs(previous.TranslatePoint(new Point(), header).X - todayButton.TranslatePoint(new Point(todayButton.ActualWidth, 0), header).X - 8) < .5,
                "Show today must sit immediately to the left of the previous-period arrow");
            var glyph = (System.Windows.Shapes.Path)previous.Template.FindName("ArrowGlyph", previous);
            var glyphCenter = glyph.TranslatePoint(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2), previous);
            Check(previous.ActualWidth >= 40 && previous.ActualHeight >= 40 && Math.Abs(glyphCenter.X - previous.ActualWidth / 2) < .5 && Math.Abs(glyphCenter.Y - previous.ActualHeight / 2) < .5,
                "Navigation arrows must have a large hit area and a centered vector glyph");
            SavePreview(window, "russian-navigation", true);
            vm.PreviousCommand.Execute(null);
            Check(!vm.ShouldShowToday, "Returning to today must hide its navigation button");
            foreach (var view in Enum.GetValues<CalendarView>())
            {
                vm.View = view; vm.TodayCommand.Execute(null);
                bool containsToday = view == CalendarView.Year
                    ? vm.Months.Any(m => m.Days.Any(d => d.Today && d.InMonth))
                    : vm.Days.Any(d => d.Today);
                Check(vm.ShouldShowToday == !containsToday, "Show today visibility must match the displayed dates in " + view);
            }
            await vm.SaveAsync();
            var saved = await new LocalStore(System.IO.Path.Combine(directory, "onboarding")).ReadAsync<WidgetSettings>("settings.bin", true);
            Check(saved?.Language == "ru", "Selected language must survive settings persistence");
            vm.Language = "en";
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check((string)todayButton.Content == "Show today" && vm.Views.Single(v => v.Value == CalendarView.Week).Title == "Week",
                "English selection must replace Russian resources without restarting");
            var sampleDay = new DayCell(new DateTime(2026, 11, 27), true, true, []);
            Check(sampleDay.Label.StartsWith("FRI") && AppLocalization.Culture.TwoLetterISOLanguageName == "en",
                "Calendar labels must keep the selected language across asynchronous dispatcher callbacks");
            vm.Language = "invalid";
            Check(vm.Language == "en", "Unsupported language values must not replace the saved selection");
        }
        finally { vm.Language = originalLanguage; }
    }
}
