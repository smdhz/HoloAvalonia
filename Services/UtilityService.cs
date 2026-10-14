using Avalonia.Threading;
using Avalonia.Controls.Notifications;
using System;
using System.Collections.ObjectModel;

namespace HoloAvalonia.Services;

public class UtilityService : ObservableBase
{
    private const int MaxLogLines = 20;
    private string _lastStatusLine = "Ready.";

    public Action<UtilityNotificationEventArgs>? NotificationSender { get; set; }

    public string LastStatusLine
    {
        get => _lastStatusLine;
        private set => SetProperty(ref _lastStatusLine, value);
    }

    public ObservableCollection<string> RecentLogs { get; } = [];

    /// <summary>
    /// Publishes a log line to the UI status area and rolling log list.
    /// </summary>
    /// <param name="message">The log message to append.</param>
    internal void PublishLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var timestamp = $"{DateTime.Now:HH:mm:ss} ";
        var line = $"{timestamp}{message}";
        var firstLineEnd = message.IndexOfAny(['\r', '\n']);
        var statusMessage = firstLineEnd >= 0 ? message[..firstLineEnd] : message;
        var statusLine = $"{timestamp}{statusMessage}";
        Dispatcher.UIThread.Post(() =>
        {
            LastStatusLine = statusLine;
            RecentLogs.Add(line);

            while (RecentLogs.Count > MaxLogLines)
            {
                RecentLogs.RemoveAt(0);
            }
        });
    }

    /// <summary>
    /// Publishes a UI notification through the active notification sink.
    /// </summary>
    /// <param name="message">The notification message body.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="type">The notification severity type.</param>
    /// <param name="expiration">Optional auto-dismiss duration.</param>
    /// <param name="onClick">Optional callback invoked when notification is clicked.</param>
    public void PublishNotification(
        string message,
        string title = "Info",
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null,
        Action? onClick = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
            NotificationSender?.Invoke(new UtilityNotificationEventArgs(message, title, type, expiration ?? TimeSpan.Zero, onClick)));
    }

    public sealed class UtilityNotificationEventArgs(
        string message,
        string title,
        NotificationType type,
        TimeSpan expiration,
        Action? onClick) : EventArgs
    {
        public string Message { get; } = message;

        public string Title { get; } = title;

        public NotificationType Type { get; } = type;

        public TimeSpan Expiration { get; } = expiration;

        public Action? OnClick { get; } = onClick;
    }
}
