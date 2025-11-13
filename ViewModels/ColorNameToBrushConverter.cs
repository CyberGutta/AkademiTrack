using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AkademiTrack.ViewModels
{
    public class ColorNameToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string colorName)
            {
                return colorName.ToLower() switch
                {
                    "green" => new SolidColorBrush(Color.Parse("#4CAF50")),
                    "orange" => new SolidColorBrush(Color.Parse("#FF9800")),
                    "blue" => new SolidColorBrush(Color.Parse("#5B9BFF")),
                    "red" => new SolidColorBrush(Color.Parse("#FF3B30")),
                    _ => new SolidColorBrush(Color.Parse("#FF9800"))
                };
            }
            return new SolidColorBrush(Color.Parse("#FF9800"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}