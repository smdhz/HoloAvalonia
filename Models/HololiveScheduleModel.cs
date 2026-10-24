using System;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace HoloAvalonia.Models;

public class HololiveScheduleModel
{
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
    public ICommand? AddToCalendarCommand { get; set; }

    [JsonIgnore]
    public ICommand? CopyIdCommand { get; set; }

    [JsonIgnore]
    public ICommand? PreviewImageCommand { get; set; }

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
}
