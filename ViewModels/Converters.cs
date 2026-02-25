using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AkademiTrack.Services;

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

    // Tab button converters
    public class BoolToBackgroundConverter : IValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return new SolidColorBrush(Color.Parse("#1A5B9BFF")); // Light blue background
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToForegroundConverter : IValueConverter
    {
        public static readonly BoolToForegroundConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return new SolidColorBrush(Color.Parse("#5B9BFF")); // Blue text
            }
            return ThemeManager.Instance.TextSecondary;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToBorderConverter : IValueConverter
    {
        public static readonly BoolToBorderConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return new SolidColorBrush(Color.Parse("#5B9BFF")); // Blue border
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Time-based positioning converters for calendar
    public class TimeToPositionConverter : IValueConverter
    {
        public static readonly TimeToPositionConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string timeStr && !string.IsNullOrEmpty(timeStr))
            {
                // Parse time like "08:15" or "0815"
                TimeSpan time;
                if (timeStr.Contains(':'))
                {
                    if (TimeSpan.TryParse(timeStr, out time))
                    {
                        // Calculate position: 08:00 = 0, each hour = 60 pixels
                        var startOfDay = new TimeSpan(8, 0, 0); // 08:00
                        var minutesFromStart = (time - startOfDay).TotalMinutes;
                        return minutesFromStart; // 1 minute = 1 pixel
                    }
                }
                else if (timeStr.Length == 4)
                {
                    // Format "0815"
                    if (int.TryParse(timeStr.Substring(0, 2), out var hours) &&
                        int.TryParse(timeStr.Substring(2, 2), out var minutes))
                    {
                        time = new TimeSpan(hours, minutes, 0);
                        
                        var startOfDay = new TimeSpan(8, 0, 0);
                        var minutesFromStart = (time - startOfDay).TotalMinutes;
                        return minutesFromStart;
                    }
                }
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DurationToHeightConverter : IMultiValueConverter
    {
        public static readonly DurationToHeightConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && values[0] is string startStr && values[1] is string endStr)
            {
                TimeSpan start = TimeSpan.Zero, end = TimeSpan.Zero;
                
                // Parse start time
                if (startStr.Contains(':'))
                {
                    TimeSpan.TryParse(startStr, out start);
                }
                else if (startStr.Length == 4)
                {
                    if (int.TryParse(startStr.Substring(0, 2), out var hours) &&
                        int.TryParse(startStr.Substring(2, 2), out var minutes))
                    {
                        start = new TimeSpan(hours, minutes, 0);
                    }
                }

                // Parse end time
                if (endStr.Contains(':'))
                {
                    TimeSpan.TryParse(endStr, out end);
                }
                else if (endStr.Length == 4)
                {
                    if (int.TryParse(endStr.Substring(0, 2), out var hours) &&
                        int.TryParse(endStr.Substring(2, 2), out var minutes))
                    {
                        end = new TimeSpan(hours, minutes, 0);
                    }
                }

                // Calculate duration in minutes, 1 minute = 1 pixel
                var durationMinutes = (end - start).TotalMinutes;
                return Math.Max(durationMinutes - 2, 20); // Minimum 20px height, -2 for margin
            }
            return 45.0;
        }
    }

    // Converter to format time from "HHmm" to "HH:mm"
    public class TimeFormatConverter : IValueConverter
    {
        public static readonly TimeFormatConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string timeStr && !string.IsNullOrEmpty(timeStr))
            {
                if (timeStr.Contains(':'))
                {
                    return timeStr; // Already formatted
                }
                else if (timeStr.Length == 4)
                {
                    // Format "0815" to "08:15"
                    return $"{timeStr.Substring(0, 2)}:{timeStr.Substring(2, 2)}";
                }
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter to convert time to Margin (for positioning)
    public class TimeToMarginConverter : IMultiValueConverter
    {
        public static readonly TimeToMarginConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 1 && values[0] is double topMargin)
            {
                return new Avalonia.Thickness(0, topMargin, 0, 0);
            }
            return new Avalonia.Thickness(0, 0, 0, 0);
        }
    }
}
