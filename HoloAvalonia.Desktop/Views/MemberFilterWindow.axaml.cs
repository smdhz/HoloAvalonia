using Avalonia.Controls;
using Avalonia.Interactivity;
using HoloAvalonia.Models;
using HoloAvalonia.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace HoloAvalonia.Views;

public partial class MemberFilterWindow : Window
{
    private readonly HololiveService? _hololiveService;

    /// <summary>
    /// Initializes the member filter window.
    /// </summary>
    public MemberFilterWindow()
    {
        InitializeComponent();
        Options = [];
        DataContext = this;
    }

    /// <summary>
    /// Initializes the member filter window with schedule data service.
    /// </summary>
    /// <param name="hololiveService">Service that provides member and visibility data.</param>
    public MemberFilterWindow(HololiveService hololiveService)
        : this()
    {
        _hololiveService = hololiveService;
        RefreshOptions();
    }

    /// <summary>
    /// Rebuilds the member visibility options from current service state.
    /// </summary>
    private void RefreshOptions()
    {
        if (_hololiveService is null)
        {
            return;
        }

        var members = _hololiveService.GetKnownMembers();
        var hiddenMembers = _hololiveService.GetHiddenMembers();

        Options.Clear();
        foreach (var option in members
            .Select(member => new MemberVisibilityOption
            {
                MemberName = member,
                MemberIcon = _hololiveService.GetMemberIcon(member),
                DisplayName = member,
                IsVisible = !hiddenMembers.Contains(member)
            }))
        {
            Options.Add(option);
        }
    }

    public ObservableCollection<MemberVisibilityOption> Options { get; }

    /// <summary>
    /// Closes the dialog without applying changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Saves the selected member visibility state and closes the dialog.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_hololiveService is null)
        {
            Close();
            return;
        }

        var hiddenMembers = Options
            .Where(x => !x.IsVisible)
            .Select(x => x.MemberName)
            .ToList();

        await _hololiveService.SaveHiddenMembersAsync(hiddenMembers);

        Close();
    }
}
