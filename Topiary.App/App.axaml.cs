using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Topiary.App.Services;
using Topiary.App.ViewModels;
using Topiary.App.Views;

namespace Topiary.App;

public partial class App : Application
{
    private IHost? _host;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Remove Avalonia data validation to avoid duplicate validations
        BindingPlugins.DataValidators.RemoveAt(0);

        ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = _host!.Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices(services =>
            {
                // Register services - Use production ResponsiveDiskScanService for reliability
                services.AddSingleton<IScanService, ResponsiveDiskScanService>();
                services.AddSingleton<IAiInsightsService, MockAiInsightsService>(); // TODO: Enable OpenAI service

                // Register ViewModels
                services.AddTransient<MainViewModel>();
            })
            .Build();
    }
}