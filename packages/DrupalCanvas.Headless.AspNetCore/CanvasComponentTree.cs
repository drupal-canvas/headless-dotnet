using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

// ASP0006 wants literal sequence numbers, which presume a static component
// template. This renderer is fully data-driven — the frame sequence IS the
// content tree — so a counter is the correct construction here, as in other
// dynamic renderers (Blazor's own DynamicComponent works this way).
#pragma warning disable ASP0006

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// Renders a Canvas component tree (<c>Page.Content</c>) through the app's
/// registered Blazor components — the Blazor counterpart of the JavaScript
/// adapters' <c>CanvasComponentTree</c>.
///
/// In draft mode (a tree marked <c>canvasDraftMode</c> by a live editor
/// session) the renderer emits the Canvas comment markers and empty-slot and
/// empty-region placeholders that give the Canvas editor measurable geometry
/// for its selection and drag-and-drop overlays; published pages keep normal
/// application markup. The marker and placeholder behavior mirrors the Astro
/// adapter's CanvasComponentTree/CanvasElement/CanvasSlot components
/// case-for-case.
/// </summary>
public sealed class CanvasComponentTree : ComponentBase
{
    /// <summary>The structured content root, or null (renders nothing but region geometry).</summary>
    [Parameter]
    public CanvasComponentTreeElement? Tree { get; set; }

    [Inject]
    private ICanvasComponentRegistry Registry { get; set; } = null!;

    [Inject]
    private ILogger<CanvasComponentTree> Logger { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var editor = CanvasRender.IsTreeDraft(Tree);
        var previewContentRegion = editor && CanvasRender.HasPreviewContentRegion(Tree);
        var emptyRegion = editor && !previewContentRegion && CanvasRender.IsTreeEmpty(Tree);
        var sequence = new Sequence();

        // A draft `full` view-mode tree with page variant chrome carries its
        // own content region element; only trees without one get top-level
        // `content` region boundaries here.
        if (editor && !previewContentRegion)
        {
            RegionMarker(builder, sequence, CanvasMarkerPosition.Start);
        }
        if (emptyRegion)
        {
            EmptyPlaceholder(builder, sequence, CanvasConstants.EmptyRegionPlaceholderClass);
        }
        if (Tree is not null)
        {
            RenderElement(builder, sequence, Tree, "tree", editor);
        }
        if (editor && !previewContentRegion)
        {
            RegionMarker(builder, sequence, CanvasMarkerPosition.End);
        }
    }

    /// <summary>
    /// Render-tree sequence numbers are meant to be static source positions;
    /// this renderer is fully data-driven, so a counter is the honest
    /// equivalent (the tree re-renders wholesale when content changes).
    /// </summary>
    private sealed class Sequence
    {
        private int _value;

        public int Next() => _value++;
    }

    private static void RegionMarker(
        RenderTreeBuilder builder, Sequence sequence, CanvasMarkerPosition position)
        => builder.AddMarkupContent(
            sequence.Next(),
            CommentMarkers.FormatComment(CanvasMarkerType.Region, position, "content"));

    private static void EmptyPlaceholder(RenderTreeBuilder builder, Sequence sequence, string cssClass)
    {
        builder.OpenElement(sequence.Next(), "div");
        builder.AddAttribute(sequence.Next(), "aria-hidden", "true");
        builder.AddAttribute(sequence.Next(), "class", cssClass);
        builder.CloseElement();
    }

    private void RenderElement(
        RenderTreeBuilder builder,
        Sequence sequence,
        CanvasComponentTreeElement node,
        string path,
        bool editor)
    {
        var componentData = CanvasRender.GetComponentRenderData(node);

        if (node.Element == CanvasRender.PreviewContentRegionElement)
        {
            // The transparent element locating routed content inside page
            // chrome: its slot children render without a DOM wrapper,
            // surrounded by the `content` region boundaries.
            if (editor)
            {
                RegionMarker(builder, sequence, CanvasMarkerPosition.Start);
                if (CanvasRender.IsTreeEmpty(node))
                {
                    EmptyPlaceholder(builder, sequence, CanvasConstants.EmptyRegionPlaceholderClass);
                }
            }
            RenderAllSlotChildren(builder, sequence, node, path, editor);
            if (editor)
            {
                RegionMarker(builder, sequence, CanvasMarkerPosition.End);
            }
            return;
        }

        if (componentData is null)
        {
            // drupal-markup wrappers and other structural elements are
            // transparent: their children render in place.
            RenderAllSlotChildren(builder, sequence, node, path, editor);
            return;
        }

        var componentType = Registry.Resolve(componentData);
        if (componentType is null)
        {
            Logger.LogError(
                "[canvas] Canvas component \"{ComponentName}\"{Instance} is not registered; "
                + "omitted subtree at \"{Path}\".",
                componentData.ComponentName,
                componentData.ComponentUuid is { } uuid ? $" (instance \"{uuid}\")" : string.Empty,
                path);
            return;
        }
        if (editor && componentData.ComponentUuid is null)
        {
            Logger.LogError(
                "[canvas] Canvas component \"{ComponentName}\" has no instance UUID; "
                + "editor markers were omitted at \"{Path}\".",
                componentData.ComponentName,
                path);
        }

        var marked = editor && componentData.ComponentUuid is { } instanceUuid;
        if (marked)
        {
            builder.AddMarkupContent(sequence.Next(), CommentMarkers.FormatComment(
                CanvasMarkerType.Component, CanvasMarkerPosition.Start, componentData.ComponentUuid!));
        }

        builder.OpenComponent(sequence.Next(), componentType);
        foreach (var (propName, value) in componentData.Props)
        {
            if (CanvasParameterBinder.MatchPropParameter(componentType, propName) is not { } parameterName)
            {
                Logger.LogWarning(
                    "[canvas] Component \"{ComponentName}\" has no parameter for prop "
                    + "\"{PropName}\"; skipped at \"{Path}\".",
                    componentData.ComponentName, propName, path);
                continue;
            }
            if (!CanvasParameterBinder.TryConvertProp(componentType, propName, value, out var converted))
            {
                Logger.LogWarning(
                    "[canvas] Prop \"{PropName}\" of \"{ComponentName}\" did not convert to "
                    + "parameter \"{ParameterName}\"; skipped at \"{Path}\".",
                    propName, componentData.ComponentName, parameterName, path);
                continue;
            }
            builder.AddComponentParameter(sequence.Next(), parameterName, converted);
        }

        foreach (var (slotName, slot) in node.Slots ?? EmptySlots)
        {
            if (CanvasParameterBinder.MatchSlotParameter(componentType, slotName) is not { } parameterName)
            {
                Logger.LogWarning(
                    "[canvas] Component \"{ComponentName}\" has no RenderFragment parameter for "
                    + "slot \"{SlotName}\"; skipped at \"{Path}\".",
                    componentData.ComponentName, slotName, path);
                continue;
            }
            var fragment = BuildSlotFragment(
                slot, slotName, $"{path}:{slotName}", editor, componentData.ComponentUuid);
            builder.AddComponentParameter(sequence.Next(), parameterName, fragment);
        }
        builder.CloseComponent();

        if (marked)
        {
            builder.AddMarkupContent(sequence.Next(), CommentMarkers.FormatComment(
                CanvasMarkerType.Component, CanvasMarkerPosition.End, componentData.ComponentUuid!));
        }
    }

    /// <summary>
    /// One slot's content, with the editor's slot boundary markers and
    /// empty-slot placeholder (the CanvasSlot.astro behavior): in editor mode
    /// an empty slot renders its placeholder instead of default markup, so
    /// Canvas can measure a drop target.
    /// </summary>
    private RenderFragment BuildSlotFragment(
        CanvasSlot slot, string slotName, string path, bool editor, string? componentUuid)
        => builder =>
        {
            var sequence = new Sequence();
            var slotId = componentUuid is null ? null : $"{componentUuid}/{slotName}";
            var empty = CanvasRender.IsSlotEmpty(slot);

            if (editor && slotId is not null)
            {
                builder.AddMarkupContent(sequence.Next(), CommentMarkers.FormatComment(
                    CanvasMarkerType.Slot, CanvasMarkerPosition.Start, slotId));
                if (empty)
                {
                    EmptyPlaceholder(builder, sequence, CanvasConstants.EmptySlotPlaceholderClass);
                }
            }
            if (!editor || !empty)
            {
                RenderSlotChildren(builder, sequence, slot, path, editor);
            }
            if (editor && slotId is not null)
            {
                builder.AddMarkupContent(sequence.Next(), CommentMarkers.FormatComment(
                    CanvasMarkerType.Slot, CanvasMarkerPosition.End, slotId));
            }
        };

    private void RenderAllSlotChildren(
        RenderTreeBuilder builder,
        Sequence sequence,
        CanvasComponentTreeElement node,
        string path,
        bool editor)
    {
        foreach (var (slotName, slot) in node.Slots ?? EmptySlots)
        {
            RenderSlotChildren(builder, sequence, slot, $"{path}:{slotName}", editor);
        }
    }

    private void RenderSlotChildren(
        RenderTreeBuilder builder, Sequence sequence, CanvasSlot slot, string path, bool editor)
    {
        var children = CanvasRender.NormalizeSlot(slot);
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case CanvasSlot.Markup markup:
                    builder.AddMarkupContent(sequence.Next(), markup.Html);
                    break;
                case CanvasSlot.Node node:
                    RenderElement(builder, sequence, node.Element, $"{path}:{index}", editor);
                    break;
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, CanvasSlot> EmptySlots =
        new Dictionary<string, CanvasSlot>();
}
