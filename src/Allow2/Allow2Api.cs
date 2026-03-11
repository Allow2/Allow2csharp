namespace Allow2;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Allow2.Models;

/// <summary>
/// API environment. Controls which server endpoints are used.
/// </summary>
public enum Allow2Environment
{
    Production,
    Staging,
    Sandbox
}

/// <summary>
/// HttpClient-based API client for the Allow2 REST API.
/// Used internally by Allow2Daemon -- not typically called directly.
/// </summary>
internal class Allow2Api
{
    private readonly HttpClient _client;
    private readonly Allow2Environment _environment;
    private readonly TimeSpan _timeout;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal Allow2Api(Allow2Environment environment, HttpClient? client = null, int timeoutMs = 15000)
    {
        _environment = environment;
        _client = client ?? new HttpClient();
        _timeout = TimeSpan.FromMilliseconds(timeoutMs);
    }

    internal string ApiUrl => _environment switch
    {
        Allow2Environment.Staging => "https://staging-api.allow2.com",
        Allow2Environment.Sandbox => "https://staging-api.allow2.com",
        _ => "https://api.allow2.com"
    };

    internal string ServiceUrl => _environment switch
    {
        Allow2Environment.Staging => "https://staging-service.allow2.com",
        Allow2Environment.Sandbox => "https://staging-service.allow2.com",
        _ => "https://service.allow2.com"
    };

    /// <summary>
    /// POST JSON to an Allow2 API endpoint.
    /// Throws Allow2Exception on HTTP 401 or non-success status codes.
    /// </summary>
    internal async Task<JsonDocument> PostAsync(string baseUrl, string path, Dictionary<string, object> body)
    {
        body["format"] = "json";
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsync(baseUrl + path, content, cts.Token);
        }
        catch (TaskCanceledException) when (cts.IsCancellationRequested)
        {
            throw new Allow2Exception(Allow2ErrorCode.Timeout, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            throw new Allow2Exception(Allow2ErrorCode.NetworkError, "Network error: " + ex.Message, ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new Allow2Exception(Allow2ErrorCode.Http401, "Credentials invalidated");
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var message = "API error " + (int)response.StatusCode;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseBody);
                if (errorDoc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    message = msgProp.GetString() ?? message;
                }
            }
            catch
            {
                // Could not parse error body -- use default message
            }
            throw new Allow2Exception(Allow2ErrorCode.ApiError, message);
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            throw new Allow2Exception(Allow2ErrorCode.InvalidResponse,
                "Unexpected non-JSON response from API", ex);
        }
    }

    /// <summary>
    /// GET from an Allow2 API endpoint.
    /// Throws Allow2Exception on HTTP 401 or non-success status codes.
    /// </summary>
    internal async Task<JsonDocument> GetAsync(string url, Dictionary<string, string>? headers = null)
    {
        using var cts = new CancellationTokenSource(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cts.Token);
        }
        catch (TaskCanceledException) when (cts.IsCancellationRequested)
        {
            throw new Allow2Exception(Allow2ErrorCode.Timeout, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            throw new Allow2Exception(Allow2ErrorCode.NetworkError, "Network error: " + ex.Message, ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new Allow2Exception(Allow2ErrorCode.Http401, "Credentials invalidated");
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var message = "API error " + (int)response.StatusCode;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseBody);
                if (errorDoc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    message = msgProp.GetString() ?? message;
                }
            }
            catch
            {
                // Could not parse error body
            }
            throw new Allow2Exception(Allow2ErrorCode.ApiError, message);
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            throw new Allow2Exception(Allow2ErrorCode.InvalidResponse,
                "Unexpected non-JSON response from API", ex);
        }
    }
}
