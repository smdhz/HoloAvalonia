using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using HoloAvalonia.Models;
using HoloAvalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Foundation;
using UIKit;

namespace HoloAvalonia.Views;

public partial class MainView : UserControl
{
    private readonly HololiveService? _hololiveService;
    private readonly ObservableCollection<MemberVisibilityOption> _filterOptions = [];
    private IInsetsManager? _insetsManager;

    /// <summary>
    /// Initializes the main view.
    /// </summary>
    public MainView()
    {
        InitializeComponent();
        FilterList.ItemsSource = _filterOptions;
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

    /// <summary>
    /// Opens the stream directly via UIKit; Avalonia's iOS launcher fails silently.
    /// </summary>
    /// <param name="sender">The clicked stream link.</param>
    /// <param name="e">Button click event arguments.</param>
    private void StreamLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: HololiveScheduleModel item }
            || NSUrl.FromString(item.StreamUrl) is not { } url)
            return;

        UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
    }

    /// <summary>
    /// Opens the member filter panel with current visibility state.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void FilterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_hololiveService is null)
            return;

        var hiddenMembers = _hololiveService.GetHiddenMembers();

        _filterOptions.Clear();
        foreach (var member in _hololiveService.GetKnownMembers())
        {
            _filterOptions.Add(new MemberVisibilityOption
            {
                MemberName = member,
                MemberIcon = _hololiveService.GetMemberIcon(member),
                DisplayName = member,
                IsVisible = !hiddenMembers.Contains(member)
            });
        }

        SetFilterPanelVisible(true);
    }

    /// <summary>
    /// Closes the member filter panel without applying changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void FilterCancelButton_Click(object? sender, RoutedEventArgs e)
    {
        SetFilterPanelVisible(false);
    }

    /// <summary>
    /// Saves the selected member visibility state and closes the panel.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private async void FilterSaveButton_Click(object? sender, RoutedEventArgs e)
    {
        SetFilterPanelVisible(false);

        if (_hololiveService is null)
            return;

        var hiddenMembers = _filterOptions
            .Where(x => !x.IsVisible)
            .Select(x => x.MemberName)
            .ToList();

        await _hololiveService.SaveHiddenMembersAsync(hiddenMembers);
    }

    /// <summary>
    /// Shows or hides the filter panel, hiding the floating button while it is open.
    /// </summary>
    /// <param name="visible">Whether the panel is shown.</param>
    private void SetFilterPanelVisible(bool visible)
    {
        FilterOverlay.IsVisible = visible;
        FilterButton.IsVisible = !visible;
    }
}
