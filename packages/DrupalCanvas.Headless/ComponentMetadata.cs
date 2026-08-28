using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrupalCanvas.Headless;

public sealed record ComponentMetadataWarning
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Project-root-relative path of the file the warning is about.</summary>
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}

public sealed record ComponentSlotMetadata
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("examples")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Examples { get; init; }
}

/// <summary>
/// One component, carrying the same metadata fields the Canvas CLI's push
/// payload carries, minus the source and compiled code fields: in the headless
/// integration the app renders its own components, so Drupal registers
/// metadata only.
/// </summary>
public sealed record ComponentMetadataEntry
{
    [JsonPropertyName("machineName")]
    public required string MachineName { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required bool Status { get; init; }

    /// <summary>Names of required props; requiredness lives outside the prop map.</summary>
    [JsonPropertyName("required")]
    public required IReadOnlyList<string> Required { get; init; }

    /// <summary>
    /// JSON-Schema-shaped prop definitions, keyed by prop name — the exact
    /// <c>props</c> map a full component create/update on the Drupal side
    /// takes.
    /// </summary>
    [JsonPropertyName("props")]
    public required IReadOnlyDictionary<string, JsonElement> Props { get; init; }

    [JsonPropertyName("slots")]
    public required IReadOnlyDictionary<string, ComponentSlotMetadata> Slots { get; init; }

    /// <summary>
    /// The component's directory relative to the component root. Diagnostic
    /// context for duplicate-machine-name conflicts; no server filesystem
    /// layout beyond the component tree leaks.
    /// </summary>
    [JsonPropertyName("relativeDirectory")]
    public required string RelativeDirectory { get; init; }
}

public sealed record ComponentMetadataPayload
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = CanvasConstants.ComponentMetadataPayloadVersion;

    /// <summary>
    /// The complete component set of the codebase. Completeness is the
    /// deletion signal: a component registered earlier but absent here no
    /// longer exists in the codebase.
    /// </summary>
    [JsonPropertyName("components")]
    public required IReadOnlyList<ComponentMetadataEntry> Components { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<ComponentMetadataWarning> Warnings { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, CanvasJson.Options);
}
