using Avalonia;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using HoloAvalonia.Services;
using HoloAvalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace HoloAvalonia;

internal class Program
{
    internal static IHost Host { get; private set; } = null!;

    /// <summary>
    /// Configures services and starts the Avalonia desktop application.
    /// </summary>
    /// <param name="args">Application startup arguments.</param>
    /// <returns>The process exit code from the Avalonia lifetime.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole();
                logging.Services.AddSingleton<ILoggerProvider, TailLogProvider>();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<BaseWebImageLoader>();
                services.AddSingleton<UtilityService>();
                services.AddSingleton<ApiTokenService>();
                services.AddSingleton<HololiveService>();
                services.AddTransient<MemberFilterWindow>();
                services.AddTransient<PasswordPromptWindow>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var imageLoader = Host.Services.GetRequiredService<BaseWebImageLoader>();
        ImageLoader.AsyncImageLoader = imageLoader;
        ImageBrushLoader.AsyncImageLoader = imageLoader;

        Host.Start();

        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        Host.StopAsync().GetAwaiter().GetResult();
        Host.Dispose();

        return exitCode;
    }

    /// <summary>
    /// Builds the Avalonia app configuration used by runtime and designer.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/> instance.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
