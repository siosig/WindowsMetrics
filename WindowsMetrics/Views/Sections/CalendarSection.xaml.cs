using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsMetrics.Views.Sections;

public partial class CalendarSection : UserControl
{
    private DateTime _displayMonth;
    private static readonly string[] DowLabels = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    public CalendarSection()
    {
        InitializeComponent();
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildDowHeader();
        RebuildCalendar();
    }

    private void BuildDowHeader()
    {
        var grid = DowHeader;
        for (int i = 0; i < 7; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < 7; i++)
        {
            var tb = new TextBlock
            {
                Text = DowLabels[i],
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                Foreground = i == 0 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x43, 0x43))
                           : i == 6 ? new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF))
                           : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }
    }

    private void RebuildCalendar()
    {
        MonthLabel.Text = _displayMonth.ToString("yyyy / MM");

        var grid = DaysGrid;
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();

        for (int i = 0; i < 7; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var today = DateTime.Today;
        var firstDay = _displayMonth;
        int startCol = (int)firstDay.DayOfWeek; // 0=Sun
        int daysInMonth = DateTime.DaysInMonth(firstDay.Year, firstDay.Month);
        int rows = (int)Math.Ceiling((startCol + daysInMonth) / 7.0);

        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(firstDay.Year, firstDay.Month, day);
            int col = ((int)date.DayOfWeek);
            int row = (startCol + day - 1) / 7;

            bool isToday = date == today;
            var cell = BuildDayCell(day, col, isToday);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }
    }

    private static Border BuildDayCell(int day, int col, bool isToday)
    {
        Brush fg = col == 0 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x43, 0x43))
                 : col == 6 ? new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF))
                 : Brushes.White;

        var tb = new TextBlock
        {
            Text = day.ToString(),
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isToday ? Brushes.Black : fg,
        };

        return new Border
        {
            Child = tb,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1),
            Background = isToday
                ? new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF))
                : Brushes.Transparent,
        };
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RebuildCalendar();
    }

    private void NextBtn_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RebuildCalendar();
    }
}
