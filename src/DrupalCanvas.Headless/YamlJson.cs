using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DrupalCanvas.Headless;

/// <summary>
/// Converts YAML documents to JSON nodes, inferring scalar types the way JSON
/// consumers of component.yml expect: plain <c>800</c> is a number, quoted
/// <c>"800"</c> is a string, plain <c>true</c>/<c>null</c> are the JSON
/// literals. (YamlDotNet's object deserializer would answer strings for
/// everything, which would corrupt JSON-Schema prop definitions such as
/// numeric example values.)
/// </summary>
internal static class YamlJson
{
    public static JsonNode? ParseDocument(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode);
    }

    public static JsonNode? Convert(YamlNode node)
        => node switch
        {
            YamlScalarNode scalar => ConvertScalar(scalar),
            YamlSequenceNode sequence => new JsonArray(
                sequence.Children.Select(Convert).ToArray()),
            YamlMappingNode mapping => ConvertMapping(mapping),
            _ => throw new InvalidOperationException($"Unsupported YAML node: {node.GetType().Name}."),
        };

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode scalarKey || scalarKey.Value is null)
            {
                throw new InvalidOperationException("YAML mapping keys must be scalars.");
            }
            result[scalarKey.Value] = Convert(value);
        }
        return result;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        // Quoted or block scalars are always strings; only plain scalars carry
        // YAML's implicit typing.
        if (scalar.Style != ScalarStyle.Plain && scalar.Style != ScalarStyle.Any)
        {
            return JsonValue.Create(value);
        }

        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating)
            && !double.IsInfinity(floating) && !double.IsNaN(floating))
        {
            return JsonValue.Create(floating);
        }

        return JsonValue.Create(value);
    }
}
