using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Topiary.Models;

namespace Topiary.Converters
{
    public class InsightTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.LargeFiles => "#FF6B6B",      // Red
                    InsightType.UnusedFiles => "#4CAF50",     // Green
                    InsightType.Redundant => "#FF9800",       // Orange
                    InsightType.TemporaryFiles => "#2196F3",  // Blue
                    InsightType.SystemHealth => "#9C27B0",    // Purple
                    InsightType.SecurityConcern => "#F44336", // Deep Red
                    _ => "#666666"
                };
            }
            return "#666666";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InsightTypeToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.LargeFiles => "#FFF5F5",
                    InsightType.UnusedFiles => "#F5FFF5",
                    InsightType.Redundant => "#FFF9F5",
                    InsightType.TemporaryFiles => "#F5F9FF",
                    InsightType.SystemHealth => "#F9F5FF",
                    InsightType.SecurityConcern => "#FFF5F5",
                    _ => "#F5F5F5"
                };
            }
            return "#F5F5F5";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InsightTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.LargeFiles => "M9,13H15V19H9V13M11,15V17H13V15H11M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3Z", // Large file
                    InsightType.UnusedFiles => "M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2M13,15V17H18V15H13M13,11V13H18V11H13M13,7V9H18V7H13M6,17H11V15H6V17M6,13H11V11H6V13M6,9H11V7H6V9Z", // Unused file
                    InsightType.Redundant => "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z", // Duplicate
                    InsightType.TemporaryFiles => "M13,9H18.5L13,3.5V9M6,2H14L20,8V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V4C4,2.89 4.89,2 6,2M15,18V16H6V18H15M18,14V12H6V14H18Z", // Temp file
                    InsightType.SystemHealth => "M3,21H21V19H3V21M3,17H21V15H3V17M3,13H21V11H3V13M3,9H21V7H3V9M3,3V5H21V3H3Z", // System
                    InsightType.SecurityConcern => "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M12,7C13.4,7 14.8,8.1 14.8,9.5V11C15.4,11 16,11.6 16,12.3V15.8C16,16.4 15.4,17 14.7,17H9.2C8.6,17 8,16.4 8,15.7V12.2C8,11.6 8.6,11 9.2,11V9.5C9.2,8.1 10.6,7 12,7M12,8.2C11.2,8.2 10.5,8.7 10.5,9.5V11H13.5V9.5C13.5,8.7 12.8,8.2 12,8.2Z", // Security
                    _ => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z" // Default circle
                };
            }
            return "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NonZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is double doubleValue)
                return doubleValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is long longValue)
                return longValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}