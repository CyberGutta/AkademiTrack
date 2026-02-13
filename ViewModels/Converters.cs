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

    // Converter for dynamic font sizing based on text length
    public class TextLengthToFontSizeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrEmpty(text))
            {
                int length = text.Length;
                
                // Dynamic font sizing based on text length
                // Base font size is 24, reduce as text gets longer
                if (length <= 10)
                    return 24.0; // Short text - full size
                else if (length <= 15)
                    return 22.0; // Medium text - slightly smaller
                else if (length <= 20)
                    return 20.0; // Long text - smaller
                else if (length <= 25)
                    return 18.0; // Very long text - much smaller
                else
                    return 16.0; // Extremely long text - smallest readable size
            }
            
            return 24.0; // Default size
        }
        
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter to check if int is greater than zero (for showing button after first failure)
    public class IntGreaterThanZeroConverter : IValueConverter
    {
        public static readonly IntGreaterThanZeroConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue > 0;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter for dynamic loading container height based on retry count
    public class IntGreaterThanZeroToHeightConverter : IMultiValueConverter
    {
        public static readonly IntGreaterThanZeroToHeightConverter Instance = new();

        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count > 0 && values[0] is int retryCount)
            {
                // If retry count > 0, show button and warning (taller)
                // Otherwise, just show loading spinner and text (shorter)
                return retryCount > 0 ? 320.0 : 240.0;
            }
            return 240.0; // Default to smaller size
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}