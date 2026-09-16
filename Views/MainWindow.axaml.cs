using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Controls.Notifications;
using HoloAvalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace HoloAvalonia.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly UtilityService? _utilityService;
    private readonly WindowNotificationManager _notificationManager;

    /// <summary>
    /// Initializes the main window and notification manager.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateThemeToggleButtonContent();
        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };
        Closed += MainWindow_Closed;
    }

    /// <summary>
    /// Initializes the main window with injected services and data context.
    /// </summary>
    /// <param name="hololiveService">The schedule service bound to the view.</param>
    /// <param name="serviceProvider">The service provider used to resolve dialogs.</param>
    /// <param name="utilityService">The utility service used for UI notifications.</param>
    public MainWindow(HololiveService hololiveService, IServiceProvider serviceProvider, UtilityService utilityService) : this()
    {
        _serviceProvider = serviceProvider;
        _utilityService = utilityService;
        _utilityService.NotificationSender = UtilityService_NotificationPublished;
        DataContext = hololiveService;
    }

    /// <summary>
    /// Opens the member filter dialog.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private async void FilterButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = _serviceProvider?.GetRequiredService<MemberFilterWindow>();
        if (dialog != null)
            await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Toggles between light and dark application themes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void ThemeToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null)
            return;

        Application.Current.RequestedThemeVariant =
            ActualThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

        UpdateThemeToggleButtonContent();
    }

    /// <summary>
    /// Updates the theme toggle button text to match the current theme.
    /// </summary>
    private void UpdateThemeToggleButtonContent()
    {
        if (ThemeToggleButton is null || Application.Current is null)
            return;

        ThemeToggleButton.Content = ActualThemeVariant == ThemeVariant.Dark
            ? "Theme: Dark"
            : "Theme: Light";
    }

    /// <summary>
    /// Displays a notification raised by <see cref="UtilityService"/>.
    /// </summary>
    /// <param name="e">Notification payload.</param>
    private void UtilityService_NotificationPublished(UtilityService.UtilityNotificationEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            _notificationManager.Show(new Notification(
                e.Title,
                e.Message,
                e.Type,
                e.Expiration,
                e.OnClick)));
    }

    /// <summary>
    /// Unsubscribes notification callbacks when the window is closed.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Window close event arguments.</param>
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_utilityService != null && _utilityService.NotificationSender == UtilityService_NotificationPublished)
        {
            _utilityService.NotificationSender = null;
        }

        Closed -= MainWindow_Closed;
    }
}
