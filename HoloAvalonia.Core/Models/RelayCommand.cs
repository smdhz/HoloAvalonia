using System;
using System.Windows.Input;

namespace HoloAvalonia.Models;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Initializes a command that delegates execute and can-execute logic.
    /// </summary>
    /// <param name="execute">Action invoked when the command executes.</param>
    /// <param name="canExecute">Optional predicate that controls command availability.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Determines whether the command can execute.
    /// </summary>
    /// <param name="parameter">Optional command parameter.</param>
    /// <returns><see langword="true"/> when execution is allowed; otherwise, <see langword="false"/>.</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <summary>
    /// Executes the command action.
    /// </summary>
    /// <param name="parameter">Optional command parameter.</param>
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Raises <see cref="CanExecuteChanged"/> to refresh command state in the UI.
    /// </summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
