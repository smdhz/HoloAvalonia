using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using HoloAvalonia.Services;
using System;

namespace HoloAvalonia.Views;

public partial class MainView : UserControl
{
    private readonly HololiveService? _hololiveService;
    private IInsetsManager? _insetsManager;

    /// <summary>
    /// Initializes the main view.
    /// </summary>
    public MainView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the main view with injected service and data context.
    /// </summary>
    /// <param name="hololiveService">The schedule service bound to the view.</param>
    public MainView(HololiveService hololiveService) : this()
    {
        _hololiveService = hololiveService;
        DataContext = hololiveService;
    }

    /// <summary>
    /// Hooks up safe-area padding and foreground resume once attached.
    /// </summary>
    /// <param name="e">Visual tree attachment event arguments.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (_insetsManager is not null)
        {
            _insetsManager.SafeAreaChanged += InsetsManager_SafeAreaChanged;
            Padding = _insetsManager.SafeAreaPadding;
        }

        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += Lifetime_Activated;
        }
    }

    /// <summary>
    /// Keeps content clear of the notch and home indicator.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Safe area change arguments.</param>
    private void InsetsManager_SafeAreaChanged(object? sender, SafeAreaChangedArgs e)
    {
        Padding = e.SafeAreaPadding;
    }

    /// <summary>
    /// Reloads and reconnects after the app returns from the background.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Activation event arguments.</param>
    private void Lifetime_Activated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Background)
        {
            _ = _hololiveService?.ResumeAsync();
        }
    }

    /// <summary>
    /// Reloads the schedule on pull-to-refresh.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Refresh request arguments.</param>
    private async void Refresher_RefreshRequested(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (_hololiveService is not null)
                await _hololiveService.ResumeAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
