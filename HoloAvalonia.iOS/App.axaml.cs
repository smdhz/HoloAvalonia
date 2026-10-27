using AsyncImageLoader;
using AsyncImageLoader.Core.Pipeline;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HoloAvalonia.Services;
using HoloAvalonia.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace HoloAvalonia;

public partial class App : Application
{
    private const string GuestPassword = "idhHZMql4FTPd7s";

    private IServiceProvider? _services;

    /// <summary>
    /// Loads XAML resources for the application.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Configures services and sets the single main view.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var imageLoader = ImageLoaderPipelineBuilder.Uncached().Build();
            ImageLoader.AsyncImageLoader = imageLoader;

            _services = BuildServices();
            // Log in with the guest password without UI; give up once it is rejected.
            _services.GetRequiredService<ApiTokenService>().PasswordPrompt =
                isRetry => Task.FromResult<string?>(isRetry ? null : GuestPassword);
            singleView.MainView = _services.GetRequiredService<MainView>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the service provider with configuration embedded in the app bundle.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildServices()
    {
        using var settingsStream = typeof(App).Assembly.GetManifestResourceStream("HoloAvalonia.appsettings.json")
            ?? throw new InvalidOperationException("Embedded appsettings.json not found.");
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(settingsStream)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<ILoggerProvider, TailLogProvider>();
        services.AddSingleton<UtilityService>();
        services.AddSingleton<ApiTokenService>();
        services.AddSingleton<HololiveService>();
        services.AddSingleton<MainView>();
        return services.BuildServiceProvider();
    }
}
