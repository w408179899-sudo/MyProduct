using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Core.Licensing;

namespace Roadhog.Infrastructure.Licensing;

public sealed class CloudflareLicenseApiClient : ILicenseApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public CloudflareLicenseApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<LicenseApiResult> ActivateAsync(
        LicenseCredential credential,
        string deviceHash,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        return SendCredentialRequestAsync(
            "api/activate",
            credential,
            deviceHash,
            clientVersion,
            cancellationToken);
    }

    public Task<LicenseApiResult> LoginAsync(
        LicenseCredential credential,
        string deviceHash,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        return SendCredentialRequestAsync(
            "api/login",
            credential,
            deviceHash,
            clientVersion,
            cancellationToken);
    }

    public async Task<LicenseApiResult> HeartbeatAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/heartbeat")
        {
            Content = JsonContent.Create(new { }, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private async Task<LicenseApiResult> SendCredentialRequestAsync(
        string path,
        LicenseCredential credential,
        string deviceHash,
        string clientVersion,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new
            {
                cdkey = credential.Cdkey,
                deviceHash,
                clientInstanceId = credential.ClientInstanceId,
                installSecret = credential.InstallSecret,
                clientVersion
            }, options: JsonOptions)
        };

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LicenseApiResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            LicenseResponseBody? body;
            try
            {
                body = JsonSerializer.Deserialize<LicenseResponseBody>(text, JsonOptions);
            }
            catch (JsonException)
            {
                return LicenseApiResult.Failure("INVALID_SERVER_RESPONSE", isTransient: true);
            }

            if (!response.IsSuccessStatusCode || body?.Success != true)
            {
                var code = string.IsNullOrWhiteSpace(body?.Error)
                    ? "HTTP_" + (int)response.StatusCode
                    : body.Error;
                return LicenseApiResult.Failure(code, IsTransient(response.StatusCode));
            }

            return new LicenseApiResult(
                true,
                false,
                null,
                body.LicenseId,
                body.ActivationGeneration,
                body.Token,
                FromUnixSeconds(body.TokenExpiresAt),
                FromUnixSeconds(body.LicenseExpiresAt),
                FromUnixSeconds(body.ServerTime));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LicenseApiResult.Failure("REQUEST_TIMEOUT", isTransient: true);
        }
        catch (HttpRequestException)
        {
            return LicenseApiResult.Failure("NETWORK_UNAVAILABLE", isTransient: true);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static DateTimeOffset? FromUnixSeconds(long? value)
    {
        if (value is null || value <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class LicenseResponseBody
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("licenseId")]
        public long? LicenseId { get; init; }

        [JsonPropertyName("activationGeneration")]
        public long? ActivationGeneration { get; init; }

        [JsonPropertyName("token")]
        public string? Token { get; init; }

        [JsonPropertyName("tokenExpiresAt")]
        public long? TokenExpiresAt { get; init; }

        [JsonPropertyName("licenseExpiresAt")]
        public long? LicenseExpiresAt { get; init; }

        [JsonPropertyName("serverTime")]
        public long? ServerTime { get; init; }
    }
}
