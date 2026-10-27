using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HoloAvalonia.Services;
using HoloAvalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace HoloAvalonia;

public partial class App : Application
{
    /// <summary>
    /// Loads XAML resources for the application.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Resolves the main window and completes framework initialization.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Program.Host.Services;
            var utilityService = services.GetRequiredService<UtilityService>();
            services.GetRequiredService<ApiTokenService>().PasswordPrompt = _ => PromptPasswordAsync(desktop);
            services.GetRequiredService<HololiveService>().ImagePreviewer =
                imageUrl => new PreviewWindow(imageUrl, utilityService).Show();

            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Displays password prompt dialog and returns entered value.
    /// </summary>
    /// <param name="desktop">Desktop lifetime used to find the owner window.</param>
    /// <returns>The entered password, or <see langword="null"/> when canceled.</returns>
    private static async Task<string?> PromptPasswordAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var owner = desktop.Windows.Count > 0
            ? desktop.Windows[0]
            : null;

        var dialog = new PasswordPromptWindow();

        if (owner is Window ownerWindow)
        {
            return await dialog.ShowDialog<string?>(ownerWindow);
        }

        dialog.Show();
        return await dialog.WaitForCloseAsync();
    }
}
