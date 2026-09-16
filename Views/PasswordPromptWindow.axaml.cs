using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace HoloAvalonia.Views;

public partial class PasswordPromptWindow : Window
{
    private readonly TaskCompletionSource<string?> _closeTcs = new();

    /// <summary>
    /// Initializes the password prompt window and related event handlers.
    /// </summary>
    public PasswordPromptWindow()
    {
        InitializeComponent();
        Opened += PasswordPromptWindow_Opened;
        Closing += PasswordPromptWindow_Closing;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Returns a task that completes when the prompt closes.
    /// </summary>
    /// <returns>The entered password, or <see langword="null"/> if canceled.</returns>
    public Task<string?> WaitForCloseAsync() => _closeTcs.Task;

    /// <summary>
    /// Focuses the password input when the window opens.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Window opened event arguments.</param>
    private void PasswordPromptWindow_Opened(object? sender, System.EventArgs e)
    {
        PasswordInput.Focus();
    }

    /// <summary>
    /// Completes the pending task as canceled if the window closes unexpectedly.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Window closing event arguments.</param>
    private void PasswordPromptWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        _closeTcs.TrySetResult(null);
    }

    /// <summary>
    /// Handles keyboard shortcuts for submit and cancel operations.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Key event arguments.</param>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitAndClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelAndClose();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Submits the current password input.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        SubmitAndClose();
    }

    /// <summary>
    /// Cancels the prompt without returning a password.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">Button click event arguments.</param>
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelAndClose();
    }

    /// <summary>
    /// Stores the entered password and closes the window.
    /// </summary>
    private void SubmitAndClose()
    {
        var password = PasswordInput.Text?.Trim();
        _closeTcs.TrySetResult(string.IsNullOrWhiteSpace(password) ? null : password);
        Close(password);
    }

    /// <summary>
    /// Marks the prompt as canceled and closes the window.
    /// </summary>
    private void CancelAndClose()
    {
        _closeTcs.TrySetResult(null);
        Close(null);
    }
}
