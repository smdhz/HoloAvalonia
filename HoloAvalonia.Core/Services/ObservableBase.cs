using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HoloAvalonia.Services;

/// <summary>
/// Base type providing <see cref="INotifyPropertyChanged"/> support with a reusable setter.
/// </summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets a backing field and raises <see cref="PropertyChanged"/> when the value changes.
    /// </summary>
    /// <returns><see langword="true"/> when updated; otherwise, <see langword="false"/>.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
