using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Topiary.Services;
using Topiary.ViewModels;
using Topiary.Views;

namespace Topiary
{
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider;
        private IConfiguration _configuration;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}", "Fatal Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

            services.AddSingleton(_configuration);
            services.AddSingleton<IDiskScanningService, DiskScanningService>();
            services.AddSingleton<IAIAnalysisService, AIAnalysisService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<MainWindow>();
        }
    }
}