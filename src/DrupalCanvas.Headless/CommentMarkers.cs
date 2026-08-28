namespace DrupalCanvas.Headless;

public enum CanvasMarkerType
{
    Component,
    Slot,
    Region,
}

public enum CanvasMarkerPosition
{
    Start,
    End,
}

/// <summary>
/// The comment markers Canvas draft previews emit around components, slots,
/// and regions, so the editor can measure their geometry for selection and
/// drag-and-drop overlays. The text format is a contract with
/// @drupal-canvas/preview-geometry's <c>parseCanvasCommentMarker()</c>.
/// </summary>
public static class CommentMarkers
{
    /// <summary>
    /// Formats the text content of a Canvas comment marker (the part between
    /// <c>&lt;!--</c> and <c>--&gt;</c>).
    /// </summary>
    public static string Format(CanvasMarkerType type, CanvasMarkerPosition position, string id)
    {
        var prefix = (type, position) switch
        {
            (CanvasMarkerType.Component, CanvasMarkerPosition.Start) => "canvas-start-",
            (CanvasMarkerType.Component, CanvasMarkerPosition.End) => "canvas-end-",
            (CanvasMarkerType.Slot, CanvasMarkerPosition.Start) => "canvas-slot-start-",
            (CanvasMarkerType.Slot, CanvasMarkerPosition.End) => "canvas-slot-end-",
            (CanvasMarkerType.Region, CanvasMarkerPosition.Start) => "canvas-region-start-",
            (CanvasMarkerType.Region, CanvasMarkerPosition.End) => "canvas-region-end-",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        return prefix + id;
    }

    /// <summary>Formats a full HTML comment carrying a Canvas marker.</summary>
    public static string FormatComment(CanvasMarkerType type, CanvasMarkerPosition position, string id)
        => $"<!-- {Format(type, position, id)} -->";
}
