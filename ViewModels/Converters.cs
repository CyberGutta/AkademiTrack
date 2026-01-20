using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AkademiTrack.ViewModels
{
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2) return 0.0;
            
            // values[1] = container width
            
            if (values[0] is string percentStr && values[1] is double containerWidth)
            {
                // Remove '%' and parse
                string numStr = percentStr.TrimEnd('%');
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage))
                {
                    return (percentage / 100.0) * containerWidth;
                }
            }
            
            return 0.0;
        }
        
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    // Converter for percentage to height (for daily boxes - 0-100)
    public class PercentageToHeightConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2) return 0.0;
            
            // values[0] = FillPercentage (0-100)
            // values[1] = container height (60px)
            
            if (values[0] is double percentage && values[1] is double containerHeight)
            {
                return (percentage / 100.0) * containerHeight;
            }
            
            return 0.0;
        }
        
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    // Converter for DayOfWeek enum to Norwegian day abbreviation
    public class DayOfWeekConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is DayOfWeek dayOfWeek)
            {
                return dayOfWeek switch
                {
                    DayOfWeek.Monday => "Man",
                    DayOfWeek.Tuesday => "Tir",
                    DayOfWeek.Wednesday => "Ons",
                    DayOfWeek.Thursday => "Tor",
                    DayOfWeek.Friday => "Fre",
                    DayOfWeek.Saturday => "Lør",
                    DayOfWeek.Sunday => "Søn",
                    _ => ""
                };
            }
            return "";
        }
        
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}