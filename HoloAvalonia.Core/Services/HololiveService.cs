using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using HoloAvalonia.Models;

namespace HoloAvalonia.Services;

public class HololiveService : ObservableBase, IDisposable
{
    private static readonly HttpClient ScheduleHttpClient = new();

    private readonly string _odataServiceRoot;
    private readonly string _odataEntitySet;
    private readonly Uri _serviceRootUri;
    private readonly string _apiBaseUrl;
    private readonly ILogger<HololiveService> _logger;
    private readonly UtilityService _uiStatusService;
    private readonly ApiTokenService _apiTokenService;
    private readonly Dictionary<string, string> _memberIcons;
    private readonly HashSet<string> _hiddenMembers = [];
    private readonly HashSet<string> _knownMembers = [];
    private readonly string _settingsFilePath;
    private readonly CancellationTokenSource _pushCts = new();
    private readonly SemaphoreSlim _loadSync = new(1, 1);
    private int _reloadRequested;
    private int _authNotificationPending;
    private TaskCompletionSource _pushReconnectSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _pushLoginSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _statusText = "Ready.";
    private bool _showPastItems;
    private bool _hideShortVideos = true;
    private bool _isReady;

    /// <summary>
    /// Initializes schedule service dependencies, local state, and background refresh loops.
    /// </summary>
    /// <param name="configuration">Application configuration source.</param>
    /// <param name="logger">Logger for schedule and push processing.</param>
    /// <param name="uiStatusService">Utility service for UI status and notifications.</param>
    /// <param name="apiTokenService">Token service used for authenticated API access.</param>
    public HololiveService(
        IConfiguration configuration,
        ILogger<HololiveService> logger,
        UtilityService uiStatusService,
        ApiTokenService apiTokenService)
    {
        _odataServiceRoot = configuration["Odata:ServiceRoot"]!;
        _odataEntitySet = configuration["Odata:EntitySet"]!;
        _serviceRootUri = new Uri(_odataServiceRoot, UriKind.Absolute);
        _apiBaseUrl = $"{_serviceRootUri.Scheme}://{_serviceRootUri.Authority}";
        _logger = logger;
        _uiStatusService = uiStatusService;
        _apiTokenService = apiTokenService;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "HoloAvalonia");
        _settingsFilePath = Path.Combine(appDirectory, "filters.json");
        _memberIcons = LoadMemberIconsFromEmbeddedResource();
        LoadFilterSettingsFromDisk();

        _uiStatusService.PropertyChanged += UiStatusService_PropertyChanged;
        StatusText = _uiStatusService.LastStatusLine;
        _ = LoadAsync();
        _ = RunPushLoopAsync(_pushCts.Token);
    }

    public ObservableCollection<HololiveScheduleModel> Items { get; } = [];

    /// <summary>
    /// Gets or sets the platform callback that shows a thumbnail preview for an image URL.
    /// </summary>
    public Action<string>? ImagePreviewer { get; set; }

    public ObservableCollection<string> RecentLogs => _uiStatusService.RecentLogs;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool ShowPastItems
    {
        get => _showPastItems;
        set
        {
            if (SetProperty(ref _showPastItems, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public bool HideShortVideos
    {
        get => _hideShortVideos;
        set
        {
            if (SetProperty(ref _hideShortVideos, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    /// <summary>
    /// Returns merged known member names from local cache, current items, and hidden-member settings.
    /// </summary>
    /// <returns>Distinct sorted member names.</returns>
    public IReadOnlyList<string> GetKnownMembers()
    {
        var members = new HashSet<string>();
        MergeMembersIntoSet(members, _knownMembers);
        MergeMembersIntoSet(members, _hiddenMembers);
        MergeMembersIntoSet(members, Items.Select(x => x.MemberName));

        return members
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>
    /// Returns the currently hidden member set.
    /// </summary>
    /// <returns>Hidden member names.</returns>
    public IReadOnlyCollection<string> GetHiddenMembers() => [.. _hiddenMembers];

    /// <summary>
    /// Formats a member name with icon prefix when available.
    /// </summary>
    /// <param name="memberName">Member name to format.</param>
    /// <returns>Formatted display name, or empty string for invalid input.</returns>
    public string FormatMemberName(string? memberName)
    {
        var normalizedName = memberName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return string.Empty;
        }
        var icon = GetMemberIcon(normalizedName);
        return string.IsNullOrWhiteSpace(icon)
            ? normalizedName
            : $"{icon}{normalizedName}";
    }

    /// <summary>
    /// Gets icon text for a member.
    /// </summary>
    /// <param name="memberName">Member name lookup key.</param>
    /// <returns>Configured icon text, or empty string when none exists.</returns>
    public string GetMemberIcon(string? memberName)
    {
        var normalizedName = memberName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return string.Empty;
        }

        if (_memberIcons.TryGetValue(normalizedName, out var icon)
            && !string.IsNullOrWhiteSpace(icon))
        {
            return icon;
        }

        return string.Empty;
    }

    /// <summary>
    /// Persists hidden-member settings and refreshes schedule data.
    /// </summary>
    /// <param name="hiddenMembers">Members to hide in the schedule view.</param>
    public async Task SaveHiddenMembersAsync(IEnumerable<string> hiddenMembers)
    {
        _hiddenMembers.Clear();
        MergeMembersIntoSet(_hiddenMembers, hiddenMembers);
        MergeMembersIntoSet(_knownMembers, _hiddenMembers);

        await SaveFilterSettingsToDiskAsync();
        await LoadAsync();
    }

    /// <summary>
    /// Requests a schedule reload. Requests made while a load is running are coalesced
    /// into one follow-up load instead of being dropped.
    /// </summary>
    public async Task LoadAsync()
    {
        Interlocked.Exchange(ref _reloadRequested, 1);

        // Re-check after release so a request arriving just before release is not lost.
        while (Volatile.Read(ref _reloadRequested) == 1 && await _loadSync.WaitAsync(0))
        {
            try
            {
                while (Interlocked.Exchange(ref _reloadRequested, 0) == 1)
                {
                    await LoadOnceAsync();
                }
            }
            finally
            {
                _loadSync.Release();
            }
        }
    }

    /// <summary>
    /// Reloads the schedule and wakes a disconnected push loop, e.g. when the app returns to the foreground.
    /// </summary>
    public Task ResumeAsync()
    {
        SetSignal(ref _pushReconnectSignal);
        return LoadAsync();
    }

    /// <summary>
    /// Loads schedule items from OData endpoint and updates UI-bound collection.
    /// </summary>
    private async Task LoadOnceAsync()
    {
        try
        {
            var thumbnailBaseUrl = $"{_apiBaseUrl}/hololive-thumbnail/";

            var now = DateTimeOffset.Now;
            var queryStart = ShowPastItems
                ? new DateTimeOffset(now.Date, now.Offset)
                : now.AddHours(-1);
            var token = await _apiTokenService.GetTokenAsync(_serviceRootUri);
            if (string.IsNullOrWhiteSpace(token))
            {
                IsReady = false;
                _logger.LogWarning("Skipping refresh because no API token is available.");
                PublishLoginRequiredNotification(
                    "Login required. Click notification to login.",
                    "Login Required");
                return;
            }

            IsReady = true;
            // Wakes the push loop if it was waiting for a login.
            SetSignal(ref _pushLoginSignal);
            var items = await QueryScheduleAsync(_odataServiceRoot, _odataEntitySet, queryStart, token);
            var hasNewKnownMember = MergeMembersIntoSet(_knownMembers, items.Select(x => x.MemberName));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                foreach (var item in items
                    .Where(x =>
                        !_hiddenMembers.Contains(x.MemberName)
                        && (!HideShortVideos || !ShouldIgnoreEntry(x.StreamTitle, x.StreamImage)))
                    .OrderBy(x => x.StartDt))
                {
                    item.StreamImage = $"{thumbnailBaseUrl}{item.Id:D}";
                    item.MemberIcon = GetMemberIcon(item.MemberName);
                    item.DisplayMemberName = item.MemberName?.Trim() ?? string.Empty;
                    item.AddToCalendarCommand = new RelayCommand(() => _ = AddToCalendarAsync(item));
                    item.CopyIdCommand = new RelayCommand(() => _ = CopyIdAsync(item));
                    item.PreviewImageCommand = new RelayCommand(() => PreviewImage(item));
                    Items.Add(item);
                }
            });

            if (hasNewKnownMember)
            {
                await SaveFilterSettingsToDiskAsync();
            }

            _logger.LogInformation("Hololive schedule refreshed. Items: {ItemCount}", Items.Count);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await ReloadAfterUnauthorizedAsync(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Hololive schedule.");
        }
    }

    /// <summary>
    /// Requests calendar event creation for a schedule item.
    /// </summary>
    /// <param name="item">Schedule item to add.</param>
    private async Task AddToCalendarAsync(HololiveScheduleModel item)
    {
        try
        {
            var token = await _apiTokenService.GetTokenAsync(_serviceRootUri);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var uri = new Uri($"{_apiBaseUrl}/api/hololive-schedule/{item.Id:D}/calendar");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers =
                {
                    Authorization = new AuthenticationHeaderValue("Bearer", token)
                }
            };
            using var response = await ScheduleHttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            _uiStatusService.PublishNotification(
                "Added to calendar.",
                "Add to Calendar",
                NotificationType.Success,
                TimeSpan.FromSeconds(3));
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await ReloadAfterUnauthorizedAsync(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add schedule {ScheduleId} to calendar.", item.Id);
            _uiStatusService.PublishNotification(
                $"Failed to add to calendar: {ex.Message}",
                "Add to Calendar",
                NotificationType.Error,
                TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>
    /// Copies the schedule identifier to the OS clipboard.
    /// </summary>
    /// <param name="item">Schedule item whose identifier is copied.</param>
    private async Task CopyIdAsync(HololiveScheduleModel item)
    {
        try
        {
            var topLevel = Application.Current?.ApplicationLifetime switch
            {
                IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
                ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
                _ => null
            };
            var clipboard = topLevel?.Clipboard
                ?? throw new InvalidOperationException("Clipboard is not available.");
            await clipboard.SetTextAsync(item.Id.ToString("D"));
            _uiStatusService.PublishNotification("ID copied to clipboard.", "Copy ID", NotificationType.Success, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy schedule {ScheduleId} to clipboard.", item.Id);
            _uiStatusService.PublishNotification($"Failed to copy ID: {ex.Message}", "Copy ID", NotificationType.Error, TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>
    /// Opens the platform thumbnail preview.
    /// </summary>
    /// <param name="item">Schedule item whose thumbnail is previewed.</param>
    private void PreviewImage(HololiveScheduleModel item)
    {
        ImagePreviewer?.Invoke(item.StreamImage);
    }

    /// <summary>
    /// Handles unauthorized responses by invalidating token and prompting re-login.
    /// </summary>
    /// <param name="ex">Unauthorized exception details.</param>
    private async Task ReloadAfterUnauthorizedAsync(Exception ex)
    {
        _logger.LogWarning(ex, "API token rejected. Invalidating token.");
        IsReady = false;
        await _apiTokenService.InvalidateTokenAsync();
        PublishLoginRequiredNotification(
            "Authentication expired. Click notification to login again.",
            "Authentication Error");
    }

    /// <summary>
    /// Publishes a clickable login notification, at most one until the user acts on it.
    /// </summary>
    /// <param name="message">Notification message.</param>
    /// <param name="title">Notification title.</param>
    private void PublishLoginRequiredNotification(string message, string title)
    {
        if (Interlocked.Exchange(ref _authNotificationPending, 1) == 1)
        {
            return;
        }

        _uiStatusService.PublishNotification(
            message,
            title,
            NotificationType.Error,
            TimeSpan.Zero,
            () => _ = RetryLoginAndReloadAsync(),
            () => Interlocked.Exchange(ref _authNotificationPending, 0));
    }

    /// <summary>
    /// Prompts for login retry and reloads schedule after successful re-authentication.
    /// </summary>
    private async Task RetryLoginAndReloadAsync()
    {
        Interlocked.Exchange(ref _authNotificationPending, 0);
        try
        {
            var token = await _apiTokenService.GetTokenAsync(_serviceRootUri, forceRenew: true);
            if (string.IsNullOrWhiteSpace(token))
            {
                IsReady = false;
                _logger.LogWarning("User canceled login after token rejection.");
                PublishLoginRequiredNotification(
                    "Login required. Click notification to login.",
                    "Login Required");
                return;
            }

            IsReady = true;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry login after token rejection.");
        }
    }

    /// <summary>
    /// Queries schedule records from OData endpoint.
    /// </summary>
    /// <param name="serviceRoot">OData service root URL.</param>
    /// <param name="entitySet">Entity set name.</param>
    /// <param name="queryStart">Lower bound for StartDt filter.</param>
    /// <param name="token">Bearer token for authorization.</param>
    /// <returns>Matched schedule entries.</returns>
    private static async Task<IEnumerable<HololiveScheduleModel>> QueryScheduleAsync(
        string serviceRoot,
        string entitySet,
        DateTimeOffset queryStart,
        string token)
    {
        var filter = FormattableString.Invariant(
            $"IsArchive eq false and StartDt ge {queryStart:yyyy-MM-dd'T'HH:mm:sszzz}");
        var requestUri = $"{serviceRoot.TrimEnd('/')}/{entitySet.Trim('/')}?$filter={Uri.EscapeDataString(filter)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", token)
            }
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await ScheduleHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(rawJson)?.AsObject();
        var valueNode = root?["value"];
        if (valueNode is null)
        {
            return [];
        }

        return valueNode.Deserialize<List<HololiveScheduleModel>>() ?? [];
    }

    /// <summary>
    /// Loads filter settings from disk.
    /// </summary>
    private void LoadFilterSettingsFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_settingsFilePath);
            try
            {
                var payload = JsonSerializer.Deserialize<FilterSettingsModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (payload is not null)
                {
                    MergeMembersIntoSet(_hiddenMembers, payload.HiddenMembers);
                    MergeMembersIntoSet(_knownMembers, payload.KnownMembers);
                    MergeMembersIntoSet(_knownMembers, _hiddenMembers);
                    return;
                }
            }
            catch
            {
                // Fall back to legacy list format.
            }

            var legacyHiddenMembers = JsonSerializer.Deserialize<List<string>>(json);
            if (legacyHiddenMembers is not null)
            {
                MergeMembersIntoSet(_hiddenMembers, legacyHiddenMembers);
                MergeMembersIntoSet(_knownMembers, _hiddenMembers);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load filter settings from {SettingsFilePath}", _settingsFilePath);
        }
    }

    /// <summary>
    /// Saves hidden and known member settings to disk.
    /// </summary>
    private async Task SaveFilterSettingsToDiskAsync()
    {
        try
        {
            var settingsDirectory = Path.GetDirectoryName(_settingsFilePath)
                ?? throw new InvalidOperationException("Unable to resolve settings directory.");
            Directory.CreateDirectory(settingsDirectory);

            var payload = new FilterSettingsModel
            {
                HiddenMembers = _hiddenMembers.OrderBy(x => x).ToList(),
                KnownMembers = _knownMembers.OrderBy(x => x).ToList()
            };
            var json = JsonSerializer.Serialize(payload);

            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save filter settings to {SettingsFilePath}", _settingsFilePath);
        }
    }

    /// <summary>
    /// Merges normalized member names into a target set.
    /// </summary>
    /// <param name="target">Target set to merge into.</param>
    /// <param name="members">Source member names.</param>
    /// <returns><see langword="true"/> when new member names were added.</returns>
    private static bool MergeMembersIntoSet(HashSet<string> target, IEnumerable<string?> members)
    {
        var changed = false;
        foreach (var member in members)
        {
            var normalizedMember = member?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedMember))
            {
                continue;
            }

            if (target.Add(normalizedMember))
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Mirrors latest utility status text to this service status property.
    /// </summary>
    /// <param name="sender">Property-changed sender.</param>
    /// <param name="e">Property-changed event arguments.</param>
    private void UiStatusService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UtilityService.LastStatusLine))
        {
            StatusText = _uiStatusService.LastStatusLine;
        }
    }

    /// <summary>
    /// Runs the websocket push listener. After a disconnect it waits until the user clicks
    /// the reconnect notification; without a token it waits until a login succeeds.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for loop shutdown.</param>
    private async Task RunPushLoopAsync(CancellationToken cancellationToken)
    {
        var wsUri = new UriBuilder(_serviceRootUri)
        {
            Scheme = _serviceRootUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/ws/hololive",
            Query = string.Empty
        }.Uri;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Reset before checking the token so a login that completes in between is not missed.
                var loginSignal = ResetSignal(ref _pushLoginSignal);

                // Never prompt from the background loop; interactive login is driven by LoadAsync and notifications.
                var token = await _apiTokenService.GetTokenAsync(
                    _serviceRootUri,
                    allowPrompt: false,
                    cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    IsReady = false;
                    await loginSignal.WaitAsync(cancellationToken);
                    continue;
                }

                IsReady = true;
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                socket.Options.CollectHttpResponseDetails = true;
                try
                {
                    await socket.ConnectAsync(wsUri, cancellationToken);
                }
                catch (WebSocketException ex) when (
                    socket.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // The login notification covers this; next iteration waits for login.
                    await ReloadAfterUnauthorizedAsync(ex);
                    continue;
                }

                _logger.LogInformation("Connected to Hololive push endpoint.");

                await ReceivePushMessagesAsync(socket, cancellationToken);
                _logger.LogWarning("Hololive push websocket closed by server.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "Hololive push websocket disconnected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Hololive push loop.");
            }

            var reconnectSignal = ResetSignal(ref _pushReconnectSignal);
            _uiStatusService.PublishNotification(
                "Push connection lost. Click notification to reconnect.",
                "Push Disconnected",
                NotificationType.Warning,
                TimeSpan.Zero,
                () => SetSignal(ref _pushReconnectSignal));
            try
            {
                await reconnectSignal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Replaces a signal with a fresh one and returns the task that completes on the next <see cref="SetSignal"/>.
    /// </summary>
    /// <param name="field">Signal field to reset.</param>
    /// <returns>Task completed by the next <see cref="SetSignal"/> call.</returns>
    private static Task ResetSignal(ref TaskCompletionSource field)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref field, signal);
        return signal.Task;
    }

    /// <summary>
    /// Completes the current signal, waking the push loop if it is waiting on it.
    /// </summary>
    /// <param name="field">Signal field to complete.</param>
    private static void SetSignal(ref TaskCompletionSource field) => Volatile.Read(ref field).TrySetResult();

    /// <summary>
    /// Receives websocket push messages and triggers notifications plus reload.
    /// </summary>
    /// <param name="socket">Connected websocket client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ReceivePushMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var messageBuffer = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", cancellationToken);
                    return;
                }

                messageBuffer.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var message = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length).Trim();
            message = BuildPushNotificationMessage(message);

            _uiStatusService.PublishNotification(message);
            _ = LoadAsync();
        }
    }

    /// <summary>
    /// Builds user-facing notification text from raw push payload.
    /// </summary>
    /// <param name="payload">Raw push payload string.</param>
    /// <returns>Formatted notification message.</returns>
    private static string BuildPushNotificationMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "New Hololive update.";
        }

        var model = TryParsePushModel(payload);
        if (model is null)
        {
            return payload.Length > 200 ? $"{payload[..200]}..." : payload;
        }

        var who = string.IsNullOrWhiteSpace(model.MemberName) ? "Unknown" : model.MemberName.Trim();
        var when = model.StartDt == default
            ? "Unknown"
            : model.StartDt.ToLocalTime().ToString("MM-dd HH:mm");
        var what = string.IsNullOrWhiteSpace(model.StreamTitle) ? "Unknown" : model.StreamTitle.Trim();
        return $"{when} {who} {what}";
    }

    /// <summary>
    /// Attempts to parse push payload into schedule model.
    /// </summary>
    /// <param name="payload">Raw push payload string.</param>
    /// <returns>Parsed model or <see langword="null"/> when parsing fails.</returns>
    private static HololiveScheduleModel? TryParsePushModel(string payload)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<HololiveScheduleModel>(payload, options);
        }
        catch
        {
            return null;
        }
    }

    public static bool ShouldIgnoreEntry(string? title, string? image)
    {
        var hasShortsTag = title?.Contains("#shorts", StringComparison.OrdinalIgnoreCase) ?? false;
        hasShortsTag = hasShortsTag || (title?.Contains("#dance", StringComparison.OrdinalIgnoreCase) ?? false);
        var hasIgnoredImage = image?.Contains("maxres2.jpg", StringComparison.OrdinalIgnoreCase) ?? false;
        return hasShortsTag || hasIgnoredImage;
    }

    /// <summary>
    /// Disposes background resources and stops push loop.
    /// </summary>
    public void Dispose()
    {
        _pushCts.Cancel();
        _pushCts.Dispose();
    }

    /// <summary>
    /// Loads member-icon mapping from embedded JSON resource.
    /// </summary>
    /// <returns>Normalized icon map keyed by member name.</returns>
    private Dictionary<string, string> LoadMemberIconsFromEmbeddedResource()
    {
        try
        {
            var assembly = typeof(HololiveService).Assembly;
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith("Resources.Icons.json", StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                _logger.LogWarning("Embedded resource not found: Resources/Icons.json");
                return [];
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("Failed to open embedded resource stream: {ResourceName}", resourceName);
                return [];
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null)
            {
                return [];
            }

            return map
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load member icons from embedded resource.");
            return [];
        }
    }

}
