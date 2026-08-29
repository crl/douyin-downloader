using System.Globalization;
using System.Windows.Data;

namespace DouyinDownloader.Helpers;

public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string selected
           && parameter is string option
           && string.Equals(selected, option, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}
