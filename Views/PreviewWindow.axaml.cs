using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using HoloAvalonia.Services;

namespace HoloAvalonia.Views;

public partial class PreviewWindow : Window
{
    private static readonly HttpClient HttpClient = new();
    private string _imageUrl = string.Empty;
    private readonly UtilityService? _notifier;
    private WriteableBitmap? _clipboardBitmap;
    private double _aspectRatio;
    private bool _isAdjustingSize;

    public PreviewWindow() : this(null)
    {
    }

    public PreviewWindow(UtilityService? notifier)
    {
        InitializeComponent();
        _notifier = notifier;
        AttachEventHandlers();
    }

    public PreviewWindow(string imageUrl, UtilityService? notifier = null) : this(notifier)
    {
        _imageUrl = imageUrl;
    }

    private void AttachEventHandlers()
    {
        Opened += PreviewWindow_Opened;
        Closed += PreviewWindow_Closed;
        SizeChanged += PreviewWindow_SizeChanged;
        PreviewImage.PointerPressed += PreviewImage_PointerPressed;
    }

    private async void PreviewWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            using var sourceBitmap = await DownloadBitmapAsync(_imageUrl);
            _clipboardBitmap = CreateClipboardSafeBitmap(sourceBitmap);
            PreviewImage.Source = _clipboardBitmap;
            _aspectRatio = sourceBitmap.Size.Width / (double)sourceBitmap.Size.Height;

            Width = Math.Min(sourceBitmap.Size.Width + 24, 1400);
            Height = Math.Min(sourceBitmap.Size.Height + 24, 900);
        }
        catch (Exception ex)
        {
            _notifier?.PublishNotification(
                $"Failed to load image: {ex.Message}",
                "Preview Thumbnail",
                NotificationType.Error,
                TimeSpan.FromSeconds(4));
        }
    }

    private void PreviewWindow_Closed(object? sender, EventArgs e)
    {
        if (_clipboardBitmap is not null)
        {
            _clipboardBitmap.Dispose();
            _clipboardBitmap = null;
        }
    }

    private void PreviewWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isAdjustingSize || _aspectRatio <= 0)
            return;

        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        var widthChanged = Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5;
        var heightChanged = Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5;

        if (!widthChanged && !heightChanged)
            return;

        _isAdjustingSize = true;
        if (widthChanged && !heightChanged)
        {
            Height = Math.Max(MinHeight, e.NewSize.Width / _aspectRatio);
        }
        else if (heightChanged && !widthChanged)
        {
            Width = Math.Max(MinWidth, e.NewSize.Height * _aspectRatio);
        }
        else
        {
            Height = Math.Max(MinHeight, e.NewSize.Width / _aspectRatio);
        }

        _isAdjustingSize = false;
    }

    private async void PreviewImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Clipboard is null || _clipboardBitmap is null)
            return;

        try
        {
            await Clipboard.SetBitmapAsync(_clipboardBitmap);
            _notifier?.PublishNotification(
                "Image copied to clipboard.",
                "Preview Thumbnail",
                NotificationType.Success,
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _notifier?.PublishNotification(
                $"Failed to copy bitmap: {ex.Message}",
                "Preview Thumbnail",
                NotificationType.Error,
                TimeSpan.FromSeconds(4));
        }
    }

    private static async Task<Bitmap> DownloadBitmapAsync(string imageUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        using var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Image endpoint returned non-image content type: {contentType ?? "unknown"}");
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync();
        await using var memoryStream = new MemoryStream();
        await sourceStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return new Bitmap(memoryStream);
    }

    private static WriteableBitmap CreateClipboardSafeBitmap(Bitmap source)
    {
        var target = new WriteableBitmap(source.PixelSize, source.Dpi);
        using var locked = target.Lock();
        source.CopyPixels(
            new PixelRect(source.PixelSize),
            locked.Address,
            locked.RowBytes * locked.Size.Height,
            locked.RowBytes);
        return target;
    }
}
