using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HoloAvalonia.Views;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HoloAvalonia.Services;

public class ApiTokenService
{
    private readonly ILogger<ApiTokenService> _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly string _tokenFilePath;
    private readonly string _tokenKeyFilePath;
    private TokenState? _state;

    /// <summary>
    /// Initializes token service dependencies and cache file locations.
    /// </summary>
    /// <param name="logger">Logger instance used for authentication flow diagnostics.</param>
    public ApiTokenService(ILogger<ApiTokenService> logger)
    {
        _logger = logger;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "HoloAvalonia");
        _tokenFilePath = Path.Combine(appDirectory, "api-token.json");
        _tokenKeyFilePath = Path.Combine(appDirectory, "api-token.key");
    }

    /// <summary>
    /// Gets a valid API token, prompting for password when required.
    /// </summary>
    /// <param name="serviceRootUri">Base service URI used to resolve token endpoint.</param>
    /// <param name="forceRenew">When <see langword="true"/>, forces a fresh login and token refresh.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A valid bearer token, or <see langword="null"/> if login is canceled.</returns>
    public async Task<string?> GetTokenAsync(Uri serviceRootUri, bool forceRenew = false, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await EnsureStateLoadedAsync();

            if (!forceRenew && HasValidToken(_state))
            {
                return _state!.Token;
            }

            _state = null;
            await DeleteTokenFileAsync();

            while (true)
            {
                var password = await PromptPasswordAsync();
                if (string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogInformation("Login canceled.");
                    return null;
                }

                try
                {
                    var response = await LoginAsync(serviceRootUri, password, cancellationToken);
                    _state = new TokenState
                    {
                        Token = response.Token,
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresInSeconds)
                    };
                    await SaveTokenFileAsync(_state);
                    _logger.LogInformation("Login succeeded.");
                    return _state.Token;
                }
                catch (HttpRequestException ex) when (
                    ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("Password incorrect, please try again.");
                }
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>
    /// Returns the cached token if still valid.
    /// </summary>
    /// <returns>The cached token, or <see langword="null"/> when missing or expired.</returns>
    public async Task<string?> GetCachedTokenAsync()
    {
        await _sync.WaitAsync();
        try
        {
            await EnsureStateLoadedAsync();
            return HasValidToken(_state) ? _state!.Token : null;
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>
    /// Clears in-memory and on-disk token cache.
    /// </summary>
    public async Task InvalidateTokenAsync()
    {
        await _sync.WaitAsync();
        try
        {
            _state = null;
            await DeleteTokenFileAsync();
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>
    /// Loads persisted token state into memory when available.
    /// </summary>
    private async Task EnsureStateLoadedAsync()
    {
        if (_state is not null)
        {
            return;
        }

        if (!File.Exists(_tokenFilePath))
        {
            return;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_tokenFilePath);
            var json = await DecryptAsync(protectedBytes);
            var state = JsonSerializer.Deserialize<TokenState>(json);
            if (state is null || string.IsNullOrWhiteSpace(state.Token))
            {
                return;
            }

            _state = state;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load token cache from {TokenFilePath}", _tokenFilePath);
        }
    }

    /// <summary>
    /// Determines whether a token state contains an unexpired token.
    /// </summary>
    /// <param name="state">Token state to validate.</param>
    /// <returns><see langword="true"/> when token exists and is still valid; otherwise, <see langword="false"/>.</returns>
    private static bool HasValidToken(TokenState? state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.Token))
        {
            return false;
        }

        if (!state.ExpiresAtUtc.HasValue)
        {
            return true;
        }

        return state.ExpiresAtUtc.Value > DateTimeOffset.UtcNow.AddMinutes(1);
    }

    /// <summary>
    /// Encrypts and saves token state to local disk.
    /// </summary>
    /// <param name="state">Token state to persist.</param>
    private async Task SaveTokenFileAsync(TokenState state)
    {
        var directory = Path.GetDirectoryName(_tokenFilePath)
            ?? throw new InvalidOperationException("Unable to resolve token cache directory.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state);
        var protectedBytes = await EncryptAsync(json);
        await File.WriteAllBytesAsync(_tokenFilePath, protectedBytes);
    }

    /// <summary>
    /// Deletes the persisted token cache file when present.
    /// </summary>
    private async Task DeleteTokenFileAsync()
    {
        await Task.Yield();
        if (!File.Exists(_tokenFilePath))
        {
            return;
        }

        try
        {
            File.Delete(_tokenFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete token cache file {TokenFilePath}", _tokenFilePath);
        }
    }

    /// <summary>
    /// Encrypts plaintext payload using locally stored symmetric key.
    /// </summary>
    /// <param name="plainText">Plaintext content.</param>
    /// <returns>Encrypted payload with version header, nonce, tag, and ciphertext.</returns>
    private async Task<byte[]> EncryptAsync(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var key = await GetOrCreateLocalKeyAsync();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[1 + nonce.Length + tag.Length + cipher.Length];
        output[0] = 1;
        Buffer.BlockCopy(nonce, 0, output, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, output, 1 + nonce.Length + tag.Length, cipher.Length);
        return output;
    }

    /// <summary>
    /// Decrypts persisted token payload.
    /// </summary>
    /// <param name="protectedBytes">Encrypted payload bytes.</param>
    /// <returns>Decrypted JSON payload.</returns>
    private async Task<string> DecryptAsync(byte[] protectedBytes)
    {
        if (protectedBytes.Length < 1 + 12 + 16 || protectedBytes[0] != 1)
        {
            throw new InvalidOperationException("Token cache payload is invalid.");
        }

        var key = await GetOrCreateLocalKeyAsync();
        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[protectedBytes.Length - 1 - nonce.Length - tag.Length];
        Buffer.BlockCopy(protectedBytes, 1, nonce, 0, nonce.Length);
        Buffer.BlockCopy(protectedBytes, 1 + nonce.Length, tag, 0, tag.Length);
        Buffer.BlockCopy(protectedBytes, 1 + nonce.Length + tag.Length, cipher, 0, cipher.Length);

        var decryptedBytes = new byte[cipher.Length];
        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipher, tag, decryptedBytes);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Gets the local encryption key or creates one if missing.
    /// </summary>
    /// <returns>A 32-byte symmetric encryption key.</returns>
    private async Task<byte[]> GetOrCreateLocalKeyAsync()
    {
        var keyDirectory = Path.GetDirectoryName(_tokenKeyFilePath)
            ?? throw new InvalidOperationException("Unable to resolve token key directory.");
        Directory.CreateDirectory(keyDirectory);

        if (File.Exists(_tokenKeyFilePath))
        {
            var existing = await File.ReadAllBytesAsync(_tokenKeyFilePath);
            if (existing.Length == 32)
            {
                return existing;
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(_tokenKeyFilePath, key);
        TryHardenFilePermissions(_tokenKeyFilePath);
        return key;
    }

    /// <summary>
    /// Attempts to restrict key file permissions on Unix systems.
    /// </summary>
    /// <param name="path">Key file path.</param>
    private static void TryHardenFilePermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Ignore permission hardening failures. App can still function.
        }
    }

    /// <summary>
    /// Performs password login and returns token response payload.
    /// </summary>
    /// <param name="serviceRootUri">Base service URI.</param>
    /// <param name="password">Password used for login.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token login response.</returns>
    private static async Task<TokenLoginResponse> LoginAsync(Uri serviceRootUri, string password, CancellationToken cancellationToken)
    {
        var loginUri = new Uri($"{serviceRootUri.Scheme}://{serviceRootUri.Authority}/api/token");
        using var client = new HttpClient();
        using var response = await client.PostAsJsonAsync(loginUri, new TokenLoginRequest(password), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Token login failed ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenLoginResponse>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new InvalidOperationException("Token login returned an empty payload.");
        }

        return payload;
    }

    /// <summary>
    /// Displays password prompt dialog and returns entered value.
    /// </summary>
    /// <returns>The entered password, or <see langword="null"/> when canceled.</returns>
    private static async Task<string?> PromptPasswordAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var owner = lifetime?.Windows.Count > 0
                ? lifetime.Windows[0]
                : null;

            var dialog = new PasswordPromptWindow();

            if (owner is Window ownerWindow)
            {
                return await dialog.ShowDialog<string?>(ownerWindow);
            }

            dialog.Show();
            return await dialog.WaitForCloseAsync();
        });
    }

    private sealed class TokenLoginRequest(string password)
    {
        /// <summary>
        /// Gets the login password sent to the token endpoint.
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; } = password;
    }

    private sealed class TokenLoginResponse
    {
        /// <summary>
        /// Gets or sets the issued bearer token.
        /// </summary>
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets token lifetime in seconds.
        /// </summary>
        [JsonPropertyName("expiresInSeconds")]
        public int ExpiresInSeconds { get; set; }
    }

    private sealed class TokenState
    {
        /// <summary>
        /// Gets or sets the cached bearer token string.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets token expiration timestamp in UTC.
        /// </summary>
        public DateTimeOffset? ExpiresAtUtc { get; set; }
    }
}
