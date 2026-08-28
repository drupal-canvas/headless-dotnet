using System.Text.Json;

namespace DrupalCanvas.Headless.Tests;

public class ComponentDiscoveryTests : IDisposable
{
    private readonly string _projectRoot;

    public ComponentDiscoveryTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), "canvas-discovery-" + Guid.NewGuid());
        Directory.CreateDirectory(_projectRoot);
    }

    public void Dispose() => Directory.Delete(_projectRoot, recursive: true);

    private void WriteComponent(string directory, string yaml, bool withEntry = true)
    {
        var fullDirectory = Path.Combine(_projectRoot, "src/components", directory);
        Directory.CreateDirectory(fullDirectory);
        File.WriteAllText(Path.Combine(fullDirectory, "component.yml"), yaml);
        if (withEntry)
        {
            File.WriteAllText(Path.Combine(fullDirectory, "Component.razor"), "<div></div>");
        }
    }

    private const string CardYaml =
        """
        name: Card
        machineName: card
        status: true
        required:
          - heading
        props:
          properties:
            heading:
              title: Heading
              type: string
              examples:
                - Feature or benefit
            width:
              title: Width
              type: integer
              examples:
                - 800
        slots: []
        """;

    [Fact]
    public void Parses_component_yml_with_json_scalar_typing()
    {
        var entry = ComponentDiscovery.ParseComponentYaml(CardYaml, "card");

        Assert.Equal("card", entry.MachineName);
        Assert.Equal("Card", entry.Name);
        Assert.True(entry.Status);
        Assert.Equal(["heading"], entry.Required);
        Assert.Empty(entry.Slots);

        var width = entry.Props["width"];
        var example = width.GetProperty("examples")[0];
        // The YAML plain scalar 800 must arrive as a JSON number, not "800".
        Assert.Equal(JsonValueKind.Number, example.ValueKind);
        Assert.Equal(800, example.GetInt32());
        Assert.Equal("Heading", entry.Props["heading"].GetProperty("title").GetString());
    }

    [Fact]
    public void Quoted_scalars_stay_strings()
    {
        var entry = ComponentDiscovery.ParseComponentYaml(
            """
            name: Card
            machineName: card
            status: true
            props:
              properties:
                variant:
                  type: string
                  examples:
                    - "800"
            """, "card");
        Assert.Equal(
            JsonValueKind.String,
            entry.Props["variant"].GetProperty("examples")[0].ValueKind);
    }

    [Fact]
    public void Parses_slot_definitions()
    {
        var entry = ComponentDiscovery.ParseComponentYaml(
            """
            name: Section
            machineName: section
            status: true
            slots:
              content:
                title: Content
                description: Main content area.
            """, "section");
        Assert.Equal("Content", entry.Slots["content"].Title);
        Assert.Equal("Main content area.", entry.Slots["content"].Description);
    }

    [Fact]
    public void Malformed_component_yml_throws_instead_of_shipping_a_broken_registry()
        => Assert.Throws<InvalidOperationException>(
            () => ComponentDiscovery.ParseComponentYaml("name: OnlyAName", "broken"));

    [Fact]
    public void Discovers_components_under_the_configured_directory()
    {
        File.WriteAllText(
            Path.Combine(_projectRoot, "canvas.config.json"),
            """{"componentDir":"src/components"}""");
        WriteComponent("card", CardYaml);
        WriteComponent("no_entry", CardYaml.Replace("machineName: card", "machineName: no_entry"),
            withEntry: false);

        var payload = ComponentDiscovery.BuildPayload(_projectRoot);

        Assert.Equal(CanvasConstants.ComponentMetadataPayloadVersion, payload.Version);
        var component = Assert.Single(payload.Components);
        Assert.Equal("card", component.MachineName);
        Assert.Equal("card", component.RelativeDirectory);
        var warning = Assert.Single(payload.Warnings);
        Assert.Equal("missing-entry-file", warning.Code);
    }

    [Fact]
    public void Duplicate_machine_names_are_all_included_and_each_flagged()
    {
        File.WriteAllText(
            Path.Combine(_projectRoot, "canvas.config.json"),
            """{"componentDir":"src/components"}""");
        WriteComponent("card", CardYaml);
        WriteComponent("card_copy", CardYaml);

        var payload = ComponentDiscovery.BuildPayload(_projectRoot);

        Assert.Equal(2, payload.Components.Count);
        Assert.Equal(2, payload.Warnings.Count(w => w.Code == "duplicate-machine-name"));
    }

    [Fact]
    public void A_missing_config_falls_back_to_the_default_directory_with_a_warning()
    {
        WriteComponent("card", CardYaml);
        var payload = ComponentDiscovery.BuildPayload(_projectRoot);
        Assert.Single(payload.Components);
        Assert.Contains(payload.Warnings, w => w.Code == "missing-canvas-config");
    }
}
