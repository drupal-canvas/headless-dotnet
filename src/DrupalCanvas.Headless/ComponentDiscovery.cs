using System.Text.Json;
using System.Text.Json.Nodes;

namespace DrupalCanvas.Headless;

/// <summary>
/// Builds the component metadata payload for a codebase, mirroring the
/// @drupal-canvas/discovery pipeline the Canvas CLI and the JavaScript
/// adapters run: resolve canvas.config.json, discover component.yml files
/// under the configured component directory, and parse their metadata.
///
/// Components without an entry file are excluded by discovery itself (with a
/// warning); duplicate machine names are all included, each flagged by a
/// warning — conflict policy belongs to the reader. Malformed component
/// metadata throws, so a broken registry never ships silently.
/// </summary>
public static class ComponentDiscovery
{
    /// <summary>The entry file extension of a .NET Canvas component.</summary>
    public static readonly IReadOnlyList<string> DefaultEntryExtensions = [".razor"];

    private const string DefaultComponentDir = "src/components";

    /// <summary>
    /// Resolves the component directory from canvas.config.json under the
    /// project root, defaulting to <c>src/components</c> like the JavaScript
    /// discovery package.
    /// </summary>
    public static string ResolveComponentDirectory(string projectRoot, ICollection<ComponentMetadataWarning>? warnings = null)
    {
        var configPath = Path.Combine(projectRoot, "canvas.config.json");
        if (!File.Exists(configPath))
        {
            warnings?.Add(new ComponentMetadataWarning
            {
                Code = "missing-canvas-config",
                Message = "No canvas.config.json found; using the default component directory.",
            });
            return Path.Combine(projectRoot, DefaultComponentDir);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var componentDir = document.RootElement.TryGetProperty("componentDir", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : DefaultComponentDir;
            return Path.Combine(projectRoot, componentDir);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"canvas.config.json is not valid JSON: {e.Message}", e);
        }
    }

    /// <summary>Builds the payload the component metadata endpoint serves.</summary>
    public static ComponentMetadataPayload BuildPayload(
        string projectRoot,
        IReadOnlyList<string>? entryExtensions = null)
    {
        var extensions = entryExtensions ?? DefaultEntryExtensions;
        var warnings = new List<ComponentMetadataWarning>();
        var componentRoot = Path.GetFullPath(ResolveComponentDirectory(projectRoot, warnings));
        var components = new List<ComponentMetadataEntry>();

        if (Directory.Exists(componentRoot))
        {
            var componentFiles = Directory
                .EnumerateFiles(componentRoot, "component.yml", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var componentFile in componentFiles)
            {
                var directory = Path.GetDirectoryName(componentFile)!;
                var relativeDirectory = Path
                    .GetRelativePath(componentRoot, directory)
                    .Replace(Path.DirectorySeparatorChar, '/');

                var hasEntry = Directory
                    .EnumerateFiles(directory)
                    .Any(file => extensions.Contains(
                        Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
                if (!hasEntry)
                {
                    warnings.Add(new ComponentMetadataWarning
                    {
                        Code = "missing-entry-file",
                        Message = $"Component directory \"{relativeDirectory}\" has a component.yml "
                            + $"but no entry file ({string.Join(", ", extensions)}); skipped.",
                        Path = relativeDirectory,
                    });
                    continue;
                }

                components.Add(ParseComponentYaml(File.ReadAllText(componentFile), relativeDirectory));
            }
        }

        foreach (var duplicates in components
            .GroupBy(component => component.MachineName)
            .Where(group => group.Count() > 1))
        {
            foreach (var component in duplicates)
            {
                warnings.Add(new ComponentMetadataWarning
                {
                    Code = "duplicate-machine-name",
                    Message = $"Machine name \"{duplicates.Key}\" is defined by more than one component.",
                    Path = component.RelativeDirectory,
                });
            }
        }

        return new ComponentMetadataPayload { Components = components, Warnings = warnings };
    }

    /// <summary>
    /// Parses one component.yml into a metadata entry, flattening the
    /// <c>props.properties</c> nesting (a component.yml file artifact): the
    /// flat map is what component create/update on the Drupal side takes.
    /// </summary>
    public static ComponentMetadataEntry ParseComponentYaml(string yaml, string relativeDirectory)
    {
        JsonNode? parsed;
        try
        {
            parsed = YamlJson.ParseDocument(yaml);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"component.yml in \"{relativeDirectory}\" is not valid YAML: {e.Message}", e);
        }

        if (parsed is not JsonObject root)
        {
            throw new InvalidOperationException(
                $"component.yml in \"{relativeDirectory}\" must be a YAML mapping.");
        }

        var machineName = RequireString(root, "machineName", relativeDirectory);
        var name = RequireString(root, "name", relativeDirectory);
        var status = root["status"]?.GetValueKind() == JsonValueKind.True;

        var required = (root["required"] as JsonArray)?
            .OfType<JsonNode>()
            .Select(node => node.GetValue<string>())
            .ToList() ?? [];

        var props = new Dictionary<string, JsonElement>();
        if (root["props"] is JsonObject propsNode && propsNode["properties"] is JsonObject properties)
        {
            foreach (var (propName, definition) in properties)
            {
                props[propName] = JsonSerializer.Deserialize<JsonElement>(
                    (definition ?? new JsonObject()).ToJsonString());
            }
        }

        var slots = new Dictionary<string, ComponentSlotMetadata>();
        if (root["slots"] is JsonObject slotsNode)
        {
            foreach (var (slotName, definition) in slotsNode)
            {
                if (definition is not JsonObject slotObject)
                {
                    throw new InvalidOperationException(
                        $"Slot \"{slotName}\" in \"{relativeDirectory}\" must be a mapping.");
                }
                slots[slotName] = new ComponentSlotMetadata
                {
                    Title = slotObject["title"]?.GetValue<string>() ?? slotName,
                    Description = slotObject["description"]?.GetValue<string>(),
                    Examples = (slotObject["examples"] as JsonArray)?
                        .OfType<JsonNode>()
                        .Select(node => node.GetValue<string>())
                        .ToList(),
                };
            }
        }
        // `slots: []` (an empty sequence) also means no slots; anything else
        // non-mapping is malformed.
        else if (root["slots"] is JsonArray slotsArray && slotsArray.Count > 0)
        {
            throw new InvalidOperationException(
                $"Slots in \"{relativeDirectory}\" must be a mapping of slot definitions.");
        }

        return new ComponentMetadataEntry
        {
            MachineName = machineName,
            Name = name,
            Status = status,
            Required = required,
            Props = props,
            Slots = slots,
            RelativeDirectory = relativeDirectory,
        };
    }

    private static string RequireString(JsonObject root, string property, string relativeDirectory)
        => root[property]?.GetValueKind() == JsonValueKind.String
            ? root[property]!.GetValue<string>()
            : throw new InvalidOperationException(
                $"component.yml in \"{relativeDirectory}\" is missing \"{property}\".");
}
