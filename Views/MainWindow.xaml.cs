using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Data;
using System.Globalization;

namespace Topiary.Views
{
    public enum InsightType
    {
        SystemHealth,
        LargeFiles
    }

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!IsRunAsAdministrator())
            {
                MessageBox.Show(
                    "This application requires administrator privileges to scan disk contents and analyze storage. Some features may not work correctly.",
                    "Administrator Rights Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private bool IsRunAsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            this.WindowState = WindowState.Normal;
        }



        

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Topiary Disk Analyzer\n\n" +
                "An open-source disk space visualization tool with AI-powered insights.\n\n" +
                "Visit our GitHub repository for more information and updates.",
                "About Topiary",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    public class InsightColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.SystemHealth => "#FFF4E6",
                    InsightType.LargeFiles => "#E6F3FF",
                    _ => "#F8F9FA"
                };
            }
            return "#F8F9FA";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InsightIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.SystemHealth => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z",
                    InsightType.LargeFiles => "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z",
                    _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InsightIconColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InsightType type)
            {
                return type switch
                {
                    InsightType.SystemHealth => "#FFA726",
                    InsightType.LargeFiles => "#0066CC",
                    _ => "#7F8C8D"
                };
            }
            return "#7F8C8D";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}