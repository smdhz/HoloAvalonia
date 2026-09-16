using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using HoloAvalonia;
using HoloAvalonia.Services;
using HoloAvalonia.Views;

namespace HoloAvalonia.Models;

public class HololiveScheduleModel
{
    private static readonly HttpClient HttpClient = new();
    private ICommand? _addToCalendarCommand;
    private ICommand? _copyIdCommand;
    private ICommand? _previewImageCommand;

    [JsonPropertyName(nameof(Id))]
    public Guid Id { get; set; }

    [JsonPropertyName(nameof(StartDt))]
    public DateTimeOffset StartDt { get; set; }

    [JsonIgnore]
    public bool IsStartWithinOneHour => Math.Abs((StartDt - DateTimeOffset.Now).TotalHours) <= 1;

    [JsonIgnore]
    public DateTimeOffset LocalStartDt => StartDt.ToLocalTime();

    [JsonPropertyName(nameof(MemberName))]
    public string MemberName { get; set; } = string.Empty;

    [JsonIgnore]
    public string MemberIcon { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayMemberName { get; set; } = string.Empty;

    [JsonIgnore]
    public ICommand AddToCalendarCommand => _addToCalendarCommand ??= new RelayCommand(PostToCalendar);

    [JsonIgnore]
    public ICommand CopyIdCommand => _copyIdCommand ??= new RelayCommand(CopyIdToClipboard);

    [JsonIgnore]
    public ICommand PreviewImageCommand => _previewImageCommand ??= new RelayCommand(PreviewImage);

    [JsonPropertyName(nameof(StreamUrl))]
    public string StreamUrl { get; set; } = string.Empty;

    [JsonPropertyName(nameof(StreamTitle))]
    public string StreamTitle { get; set; } = string.Empty;

    [JsonPropertyName(nameof(StreamImage))]
    public string StreamImage { get; set; } = string.Empty;

    [JsonPropertyName(nameof(Md5))]
    public string Md5 { get; set; } = string.Empty;

    [JsonPropertyName(nameof(IsArchive))]
    public bool IsArchive { get; set; }

    [JsonPropertyName(nameof(CreatedAt))]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Requests calendar creation through the Forge API.
    /// </summary>
    private async void PostToCalendar()
    {
        var services = Program.Host.Services;
        var notifier = services.GetService(typeof(UtilityService)) as UtilityService;
        var configuration = services.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))
            as Microsoft.Extensions.Configuration.IConfiguration;
        var tokenService = services.GetService(typeof(ApiTokenService)) as ApiTokenService;
        var serviceRootUri = new Uri(configuration!["Odata:ServiceRoot"]!, UriKind.Absolute);
        var token = await tokenService!.GetTokenAsync(serviceRootUri);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var uri = new Uri(
            $"{serviceRootUri.Scheme}://{serviceRootUri.Authority}/api/hololive-schedule/{Id:D}/calendar");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", token)
            }
        };
        using var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        notifier?.PublishNotification("Added to calendar.", "Add to Calendar", NotificationType.Success, TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Copies the schedule identifier to the OS clipboard.
    /// </summary>
    private async void CopyIdToClipboard()
    {
        var services = Program.Host.Services;
        var notifier = services.GetService(typeof(UtilityService)) as UtilityService;
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        await desktop.MainWindow!.Clipboard!.SetTextAsync(Id.ToString("D"));
        notifier?.PublishNotification("ID copied to clipboard.", "Copy ID", NotificationType.Success, TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Opens the dedicated thumbnail preview window.
    /// </summary>
    private void PreviewImage()
    {
        var services = Program.Host.Services;
        var notifier = services.GetService(typeof(UtilityService)) as UtilityService;
        new PreviewWindow(StreamImage, notifier).Show();
    }
}
