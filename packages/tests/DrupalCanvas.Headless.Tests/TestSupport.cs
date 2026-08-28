using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using DrupalCanvas.Headless;

namespace DrupalCanvas.Headless.Tests;

/// <summary>
/// A fake HTTP handler that answers from a queue (the last response repeats)
/// and records every request with its form/body content, standing in for the
/// vitest fetch mocks of the JavaScript SDK's test suite.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    private Func<HttpResponseMessage>? _lastFactory;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public bool ThrowNetworkError { get; set; }

    public void Enqueue(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        _lastFactory = response;
    }

    public void EnqueueJson(int status, string json)
        => Enqueue(() => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));

        if (ThrowNetworkError)
        {
            throw new HttpRequestException("refused");
        }

        var factory = _responses.Count > 0
            ? _responses.Dequeue()
            : _lastFactory ?? throw new InvalidOperationException("No response enqueued.");
        if (_responses.Count == 0 && _lastFactory is null)
        {
            _lastFactory = factory;
        }
        return factory();
    }

    public HttpClient CreateClient() => new(this);

    public Dictionary<string, string> FormBody(int index = 0)
        => Requests[index].Body
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "");
}

/// <summary>The in-memory adapter of the JavaScript flows tests, ported.</summary>
public sealed class TestAdapter : IDraftServerAdapter
{
    public const string FlagCookie = "__test_bypass";

    public Dictionary<string, DraftCookie> Cookies { get; } = [];

    public bool Flag { get; private set; }

    public ValueTask<string?> GetCookieAsync(string name)
        => ValueTask.FromResult(Cookies.TryGetValue(name, out var cookie) ? cookie.Value : null);

    public ValueTask SetCookieAsync(DraftCookie cookie)
    {
        Cookies[cookie.Name] = cookie;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> IsDraftFlagEnabledAsync() => ValueTask.FromResult(Flag);

    public ValueTask EnableDraftFlagAsync()
    {
        Flag = true;
        // Real frameworks set their flag cookie with default attributes; the
        // flows are expected to re-set it cross-site.
        if (!Cookies.ContainsKey(FlagCookie))
        {
            Cookies[FlagCookie] = new DraftCookie
            {
                Name = FlagCookie,
                Value = "bypass-value",
                Secure = false,
                Partitioned = false,
            };
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisableDraftFlagAsync()
    {
        Flag = false;
        return ValueTask.CompletedTask;
    }

    public string? DraftFlagCookieName => FlagCookie;

    public FlowResponse Redirect(string path) => FlowResponse.Redirect(path, 307);

    public void SeedSession(DraftData draftData)
    {
        Flag = true;
        Cookies[CanvasConstants.DraftDataCookieName] =
            DraftCookie.Build(CanvasConstants.DraftDataCookieName, draftData.Serialize());
    }
}

public static class TestData
{
    public static readonly DraftConfig Config = new() { BaseUrl = "https://drupal.example" };

    public static readonly Dictionary<string, object?> ValidClaims = new()
    {
        ["path"] = "/node/1",
        ["resourceVersion"] = "rel:working-copy",
        ["previewContext"] = new Dictionary<string, object?>
        {
            ["viewMode"] = "teaser",
            ["pageVariant"] = "alternate",
        },
        ["sub"] = "42",
        ["renewUrl"] = "https://drupal.example/canvas-headless/renew",
    };

    public static Dictionary<string, object?> Claims(params (string Key, object? Value)[] overrides)
    {
        var claims = new Dictionary<string, object?>(ValidClaims);
        foreach (var (key, value) in overrides)
        {
            if (value is UnsetMarker)
            {
                claims.Remove(key);
            }
            else
            {
                claims[key] = value;
            }
        }
        return claims;
    }

    public sealed record UnsetMarker;

    public static readonly UnsetMarker Unset = new();

    public static string BuildAssertion(IReadOnlyDictionary<string, object?> claims)
    {
        static string Encode(object value)
            => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
        return $"{Encode(new { alg = "RS256" })}.{Encode(claims)}.signature";
    }

    public const string TokenResponseJson =
        """{"token_type":"Bearer","expires_in":900,"access_token":"access-token-value"}""";

    public static DraftData LiveDraftData(
        string? path = null,
        string? sub = null,
        long? tokenExpiresAt = null,
        DraftPreviewContext? previewContext = null)
        => new()
        {
            Path = path ?? "/node/9",
            ResourceVersion = "rel:working-copy",
            PreviewContext = previewContext,
            Sub = sub ?? "42",
            RenewUrl = "https://drupal.example/canvas-headless/renew",
            AccessToken = "old-token",
            TokenType = "Bearer",
            TokenExpiresAt = tokenExpiresAt
                ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600_000,
            CodeVerifier = "stored-verifier",
        };

    public static (DraftServer Server, TestAdapter Adapter, FakeHttpHandler Http) MakeServer(
        DraftConfig? config = null)
    {
        var adapter = new TestAdapter();
        var http = new FakeHttpHandler();
        var server = new DraftServer(adapter, http.CreateClient(), () => config ?? Config);
        return (server, adapter, http);
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
