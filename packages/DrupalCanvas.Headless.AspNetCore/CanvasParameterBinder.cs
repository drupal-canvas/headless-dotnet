using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Components;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// Converts a Canvas node's wire props (JSON values keyed by camelCase or
/// snake_case names) into a Blazor component's [Parameter] values, and matches
/// wire slot names to RenderFragment parameters. Name matching ignores case
/// and the <c>._:-</c> separators, so the wire prop <c>headingElement</c>, a
/// yml prop <c>heading_element</c>, and a C# parameter <c>HeadingElement</c>
/// all meet.
/// </summary>
public static class CanvasParameterBinder
{
    private sealed record ComponentParameters(
        IReadOnlyDictionary<string, PropertyInfo> Values,
        IReadOnlyDictionary<string, PropertyInfo> Fragments);

    private static readonly ConcurrentDictionary<Type, ComponentParameters> Cache = new();

    private static readonly JsonSerializerOptions BindOptions = new(JsonSerializerDefaults.Web);

    private static string Normalize(string name)
        => new string(name.Where(c => c is not ('.' or ':' or '_' or '-')).ToArray())
            .ToLowerInvariant();

    private static ComponentParameters Describe(Type componentType)
        => Cache.GetOrAdd(componentType, type =>
        {
            var values = new Dictionary<string, PropertyInfo>();
            var fragments = new Dictionary<string, PropertyInfo>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<ParameterAttribute>() is null || !property.CanWrite)
                {
                    continue;
                }
                var key = Normalize(property.Name);
                if (property.PropertyType == typeof(RenderFragment))
                {
                    fragments[key] = property;
                }
                else
                {
                    values[key] = property;
                }
            }
            return new ComponentParameters(values, fragments);
        });

    /// <summary>The parameter name for one wire prop, or null when the component has none.</summary>
    public static string? MatchPropParameter(Type componentType, string propName)
        => Describe(componentType).Values.TryGetValue(Normalize(propName), out var property)
            ? property.Name
            : null;

    /// <summary>The RenderFragment parameter name for one wire slot, or null.</summary>
    public static string? MatchSlotParameter(Type componentType, string slotName)
        => Describe(componentType).Fragments.TryGetValue(Normalize(slotName), out var property)
            ? property.Name
            : null;

    /// <summary>
    /// Converts one wire prop value to the parameter's CLR type. Strings bind
    /// to <see cref="MarkupString"/> parameters as markup; everything else
    /// deserializes with web defaults (case-insensitive, camelCase-friendly).
    /// Returns false when the value does not convert — the caller skips the
    /// parameter, leaving the component's default.
    /// </summary>
    public static bool TryConvertProp(
        Type componentType, string propName, JsonElement value, out object? converted)
    {
        converted = null;
        if (!Describe(componentType).Values.TryGetValue(Normalize(propName), out var property))
        {
            return false;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (targetType == typeof(MarkupString))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            converted = new MarkupString(value.GetString()!);
            return true;
        }

        try
        {
            converted = value.Deserialize(property.PropertyType, BindOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
