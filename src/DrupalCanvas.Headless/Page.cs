using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrupalCanvas.Headless;

/// <summary>
/// Rendered-page contracts describing the JSON returned by Drupal's
/// rendered-content endpoint. The wire shapes match the JavaScript SDK's
/// <c>page.ts</c> exactly.
/// </summary>
public abstract record PageResult
{
    /// <summary>Parses Drupal's content-or-redirect answer for one request URI.</summary>
    public static PageResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var isRedirect = document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("redirect", out _);
        return isRedirect
            ? JsonSerializer.Deserialize<PageRedirect>(json, CanvasJson.Options)!
            : JsonSerializer.Deserialize<Page>(json, CanvasJson.Options)!;
    }
}

/// <summary>Drupal's resolved-and-rendered answer for a request URI.</summary>
public sealed record Page : PageResult
{
    /// <summary>
    /// The Canvas component tree, or null when Canvas does not return managed
    /// content for the route.
    /// </summary>
    [JsonPropertyName("content")]
    public CanvasComponentTreeElement? Content { get; init; }

    [JsonPropertyName("head")]
    public required PageHead Head { get; init; }

    [JsonPropertyName("route")]
    public required DrupalRoute Route { get; init; }
}

/// <summary>A redirect Drupal resolved before routed content.</summary>
public sealed record PageRedirect : PageResult
{
    [JsonPropertyName("redirect")]
    public required PageRedirectTarget Redirect { get; init; }
}

public sealed record PageRedirectTarget
{
    [JsonPropertyName("external")]
    public required bool External { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Drupal's configured redirect status code.</summary>
    [JsonPropertyName("statusCode")]
    public required int StatusCode { get; init; }
}

/// <summary>The filtered Unhead-compatible document head returned by Drupal.</summary>
public sealed record PageHead
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Scalar attributes for document meta tags.</summary>
    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyDictionary<string, string>>? Meta { get; init; }

    /// <summary>Scalar attributes for non-stylesheet document link tags.</summary>
    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyDictionary<string, string>>? Link { get; init; }

    /// <summary>Inert JSON-LD data scripts ({ type, textContent }).</summary>
    [JsonPropertyName("script")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JsonElement>? Script { get; init; }
}

/// <summary>Identity-only metadata for the rendered Drupal entity.</summary>
public sealed record DrupalRouteEntity
{
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    [JsonPropertyName("bundle")]
    public required string Bundle { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("langcode")]
    public required string Langcode { get; init; }
}

/// <summary>The Drupal route that was resolved for the requested frontend URI.</summary>
public sealed record DrupalRoute
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("requestUri")]
    public required string RequestUri { get; init; }

    [JsonPropertyName("params")]
    public IReadOnlyDictionary<string, string> Params { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Whether Canvas manages the route's complete component tree.</summary>
    [JsonPropertyName("managedByCanvas")]
    public required bool ManagedByCanvas { get; init; }

    [JsonPropertyName("entity")]
    public DrupalRouteEntity? Entity { get; init; }
}

/// <summary>
/// One element of the rendered content tree: element name, scalar props, and
/// slots containing rendered markup or nested elements.
/// </summary>
public sealed record CanvasComponentTreeElement
{
    /// <summary>
    /// Structural elements include <c>renderless-container</c> and the
    /// draft-only <c>canvas-preview-content-region</c> that locates routed
    /// content inside page chrome.
    /// </summary>
    [JsonPropertyName("element")]
    public required string Element { get; init; }

    [JsonPropertyName("props")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, JsonElement>? Props { get; init; }

    [JsonPropertyName("slots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, CanvasSlot>? Slots { get; init; }

    /// <summary>SDK render context: present while the draft/editor session is enabled.</summary>
    [JsonPropertyName("canvasDraftMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanvasDraftMode { get; init; }
}

/// <summary>
/// A slot value emitted by the Custom Elements API. A slot with one child is
/// serialized as that value; a multi-value slot is serialized as an array.
/// Drupal render arrays can preserve nested child groups, so arrays may be
/// nested while retaining their render order.
/// </summary>
[JsonConverter(typeof(CanvasSlotJsonConverter))]
public abstract record CanvasSlot
{
    /// <summary>Rendered HTML markup.</summary>
    public sealed record Markup(string Html) : CanvasSlot;

    /// <summary>A nested structured element.</summary>
    public sealed record Node(CanvasComponentTreeElement Element) : CanvasSlot;

    /// <summary>A nested child group, order-preserving.</summary>
    public sealed record Group(IReadOnlyList<CanvasSlot> Children) : CanvasSlot;
}

internal sealed class CanvasSlotJsonConverter : JsonConverter<CanvasSlot>
{
    public override CanvasSlot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => new CanvasSlot.Markup(reader.GetString()!),
            JsonTokenType.StartObject => new CanvasSlot.Node(
                JsonSerializer.Deserialize<CanvasComponentTreeElement>(ref reader, options)!),
            JsonTokenType.StartArray => ReadGroup(ref reader, options),
            _ => throw new JsonException($"Unexpected slot token: {reader.TokenType}."),
        };

    private static CanvasSlot.Group ReadGroup(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var children = new List<CanvasSlot>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            children.Add(JsonSerializer.Deserialize<CanvasSlot>(ref reader, options)!);
        }
        return new CanvasSlot.Group(children);
    }

    public override void Write(Utf8JsonWriter writer, CanvasSlot value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case CanvasSlot.Markup markup:
                writer.WriteStringValue(markup.Html);
                break;
            case CanvasSlot.Node node:
                JsonSerializer.Serialize(writer, node.Element, options);
                break;
            case CanvasSlot.Group group:
                writer.WriteStartArray();
                foreach (var child in group.Children)
                {
                    JsonSerializer.Serialize(writer, child, options);
                }
                writer.WriteEndArray();
                break;
        }
    }
}

/// <summary>Shared serializer options for the Canvas wire formats.</summary>
public static class CanvasJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
