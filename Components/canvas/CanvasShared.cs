using System.Text.Json.Serialization;

namespace CanvasApp.Canvas;

/// <summary>An image prop value ({ src, alt?, width?, height? }) on the Canvas wire.</summary>
public sealed record CanvasImage
{
    [JsonPropertyName("src")]
    public string? Src { get; init; }

    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }
}

/// <summary>A video prop value ({ src, poster? }) on the Canvas wire.</summary>
public sealed record CanvasVideo
{
    [JsonPropertyName("src")]
    public string? Src { get; init; }

    [JsonPropertyName("poster")]
    public string? Poster { get; init; }
}

/// <summary>
/// The counterpart of Astro's <c>class:list</c>: joins class fragments,
/// dropping null and empty entries.
/// </summary>
public static class Css
{
    public static string Cx(params string?[] parts)
        => string.Join(' ', parts.Where(part => !string.IsNullOrEmpty(part)));
}
