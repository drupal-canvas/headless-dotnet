using System.Text.Json;
using System.Text.RegularExpressions;

namespace DrupalCanvas.Headless;

/// <summary>
/// Component metadata separated from the props an app component receives.
/// </summary>
/// <param name="Element">The custom-element name emitted by Drupal, e.g. <c>js-hello-card</c>.</param>
/// <param name="ComponentName">The app registry key, e.g. <c>hello-card</c>.</param>
/// <param name="ComponentUuid">The Canvas component instance UUID, when Drupal included it.</param>
/// <param name="Props">Props safe to pass to the app component.</param>
public sealed record CanvasComponentRenderData(
    string Element,
    string ComponentName,
    string? ComponentUuid,
    IReadOnlyDictionary<string, JsonElement> Props);

/// <summary>
/// Framework-neutral helpers for rendering Canvas Custom Elements trees.
/// Framework bindings use these helpers to resolve app-owned components
/// consistently and to preserve Canvas instance identity. A port of the
/// JavaScript SDK's <c>render.ts</c>.
/// </summary>
public static partial class CanvasRender
{
    /// <summary>The prop used on the wire for a Canvas component instance UUID.</summary>
    public const string ComponentUuidProp = "canvasUuid";

    /// <summary>Renderless wire element locating routed content inside page chrome.</summary>
    public const string PreviewContentRegionElement = "canvas-preview-content-region";

    /// <summary>
    /// Converts a slot's single-or-multiple wire shape to one flat, iterable
    /// list of leaf children (markup strings and structured elements).
    /// </summary>
    public static IReadOnlyList<CanvasSlot> NormalizeSlot(CanvasSlot? slot)
    {
        var children = new List<CanvasSlot>();
        Collect(slot, children);
        return children;

        static void Collect(CanvasSlot? value, List<CanvasSlot> into)
        {
            switch (value)
            {
                case null:
                    break;
                case CanvasSlot.Group group:
                    foreach (var child in group.Children)
                    {
                        Collect(child, into);
                    }
                    break;
                default:
                    into.Add(value);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether a slot has no Canvas child components and needs an editor
    /// placeholder. String-only values are component defaults/examples, which
    /// editor rendering replaces with the empty-slot placeholder.
    /// </summary>
    public static bool IsSlotEmpty(CanvasSlot? slot)
    {
        var children = NormalizeSlot(slot);
        return children.Count == 0 || children.All(IsSlotDefaultChild);
    }

    /// <summary>Whether a top-level Canvas region has no rendered page content.</summary>
    public static bool IsTreeEmpty(CanvasComponentTreeElement? tree)
        => tree is null || IsElementEmpty(tree);

    private static bool IsElementEmpty(CanvasComponentTreeElement element)
    {
        if (GetComponentRenderData(element) is not null)
        {
            return false;
        }
        return (element.Slots ?? EmptySlots).Values.All(slot =>
            NormalizeSlot(slot).All(child => child switch
            {
                CanvasSlot.Markup markup => markup.Html.Trim().Length == 0,
                CanvasSlot.Node node => IsElementEmpty(node.Element),
                _ => true,
            }));
    }

    /// <summary>Whether the structured root node was marked as draft output.</summary>
    public static bool IsTreeDraft(CanvasComponentTreeElement? tree)
        => tree?.CanvasDraftMode == true;

    /// <summary>Whether a tree contains an explicit page content region.</summary>
    public static bool HasPreviewContentRegion(CanvasComponentTreeElement? tree)
    {
        if (tree is null)
        {
            return false;
        }
        if (tree.Element == PreviewContentRegionElement)
        {
            return true;
        }
        return (tree.Slots ?? EmptySlots).Values.Any(slot =>
            NormalizeSlot(slot).Any(child =>
                child is CanvasSlot.Node node && HasPreviewContentRegion(node.Element)));
    }

    /// <summary>Whether one slot child is default markup rather than a Canvas component.</summary>
    private static bool IsSlotDefaultChild(CanvasSlot child)
    {
        if (child is CanvasSlot.Markup)
        {
            return true;
        }
        if (child is not CanvasSlot.Node { Element: var element } || element.Element != "drupal-markup")
        {
            return false;
        }
        return (element.Slots ?? EmptySlots).Values.All(slot =>
            NormalizeSlot(slot).All(IsSlotDefaultChild));
    }

    /// <summary>
    /// Gets the app registry key from a Drupal component custom-element name.
    ///
    /// Canvas external Code Components use the <c>js.</c> component source ID.
    /// The Custom Elements API converts that to a valid element name with a
    /// <c>js-</c> prefix. Registry keys intentionally remain the component.yml
    /// machine name.
    /// </summary>
    public static string? ComponentNameFromElement(string element)
        => element.StartsWith("js-", StringComparison.Ordinal) && element.Length > 3
            ? element[3..]
            : null;

    /// <summary>
    /// Converts a component.yml machine name to Drupal's custom-element name.
    /// Custom element names are lowercase, so registry lookup must compare
    /// this normalized value instead of assuming the wire format preserved
    /// casing.
    /// </summary>
    public static string ComponentElementFromName(string componentName)
        => "js-" + MachineNameSeparators().Replace(componentName, "-").ToLowerInvariant();

    [GeneratedRegex("[.:_]")]
    private static partial Regex MachineNameSeparators();

    /// <summary>Resolves a component implementation without losing camelCase machine names.</summary>
    public static T? FindComponent<T>(
        IReadOnlyDictionary<string, T> components,
        CanvasComponentRenderData data)
        where T : class
    {
        if (components.TryGetValue(data.ComponentName, out var direct))
        {
            return direct;
        }
        return components
            .FirstOrDefault(entry => ComponentElementFromName(entry.Key) == data.Element)
            .Value;
    }

    /// <summary>
    /// Resolves an app-owned component and removes Canvas-only metadata from
    /// its props. Non-component structural elements return null.
    /// </summary>
    public static CanvasComponentRenderData? GetComponentRenderData(CanvasComponentTreeElement node)
    {
        var componentName = ComponentNameFromElement(node.Element);
        if (componentName is null)
        {
            return null;
        }

        var props = new Dictionary<string, JsonElement>(
            node.Props ?? new Dictionary<string, JsonElement>());
        string? componentUuid = null;
        if (props.TryGetValue(ComponentUuidProp, out var uuidValue))
        {
            props.Remove(ComponentUuidProp);
            if (uuidValue.ValueKind == JsonValueKind.String && uuidValue.GetString() is { Length: > 0 } uuid)
            {
                componentUuid = uuid;
            }
        }

        return new CanvasComponentRenderData(node.Element, componentName, componentUuid, props);
    }

    private static readonly IReadOnlyDictionary<string, CanvasSlot> EmptySlots =
        new Dictionary<string, CanvasSlot>();
}
