using System.Text.Json;

namespace DrupalCanvas.Headless.Tests;

public class CanvasRenderTests
{
    private static CanvasComponentTreeElement Element(
        string element,
        Dictionary<string, JsonElement>? props = null,
        Dictionary<string, CanvasSlot>? slots = null)
        => new() { Element = element, Props = props, Slots = slots };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void NormalizeSlot_flattens_nested_groups_in_order()
    {
        var slot = new CanvasSlot.Group([
            new CanvasSlot.Markup("<p>one</p>"),
            new CanvasSlot.Group([
                new CanvasSlot.Node(Element("js-card")),
                new CanvasSlot.Markup("<p>two</p>"),
            ]),
        ]);
        var children = CanvasRender.NormalizeSlot(slot);
        Assert.Equal(3, children.Count);
        Assert.IsType<CanvasSlot.Markup>(children[0]);
        Assert.IsType<CanvasSlot.Node>(children[1]);
        Assert.Equal("<p>two</p>", Assert.IsType<CanvasSlot.Markup>(children[2]).Html);
    }

    [Fact]
    public void A_missing_slot_normalizes_to_no_children()
        => Assert.Empty(CanvasRender.NormalizeSlot(null));

    [Fact]
    public void String_only_slots_count_as_empty_for_editor_placeholders()
    {
        Assert.True(CanvasRender.IsSlotEmpty(new CanvasSlot.Markup("default example")));
        Assert.False(CanvasRender.IsSlotEmpty(new CanvasSlot.Node(Element("js-card"))));
    }

    [Fact]
    public void Drupal_markup_wrappers_without_components_count_as_empty()
    {
        var markupOnly = new CanvasSlot.Node(Element("drupal-markup", slots: new()
        {
            ["default"] = new CanvasSlot.Markup("<p>example</p>"),
        }));
        Assert.True(CanvasRender.IsSlotEmpty(markupOnly));

        var withComponent = new CanvasSlot.Node(Element("drupal-markup", slots: new()
        {
            ["default"] = new CanvasSlot.Node(Element("js-card")),
        }));
        Assert.False(CanvasRender.IsSlotEmpty(withComponent));
    }

    [Fact]
    public void An_empty_tree_is_detected_through_structural_nesting()
    {
        Assert.True(CanvasRender.IsTreeEmpty(null));
        Assert.True(CanvasRender.IsTreeEmpty(Element("renderless-container", slots: new()
        {
            ["default"] = new CanvasSlot.Markup("  "),
        })));
        Assert.False(CanvasRender.IsTreeEmpty(Element("renderless-container", slots: new()
        {
            ["default"] = new CanvasSlot.Node(Element("js-card")),
        })));
    }

    [Fact]
    public void The_preview_content_region_is_found_anywhere_in_the_tree()
    {
        Assert.False(CanvasRender.HasPreviewContentRegion(Element("renderless-container")));
        Assert.True(CanvasRender.HasPreviewContentRegion(Element("renderless-container", slots: new()
        {
            ["default"] = new CanvasSlot.Node(Element("canvas-preview-content-region")),
        })));
    }

    [Theory]
    [InlineData("js-hello-card", "hello-card")]
    [InlineData("js-", null)]
    [InlineData("drupal-markup", null)]
    [InlineData("renderless-container", null)]
    public void Component_names_come_from_the_js_element_prefix(string element, string? expected)
        => Assert.Equal(expected, CanvasRender.ComponentNameFromElement(element));

    [Theory]
    [InlineData("hello-card", "js-hello-card")]
    [InlineData("card_container", "js-card-container")]
    [InlineData("my.card:big", "js-my-card-big")]
    [InlineData("CamelCard", "js-camelcard")]
    public void Machine_names_convert_to_custom_element_names(string name, string expected)
        => Assert.Equal(expected, CanvasRender.ComponentElementFromName(name));

    [Fact]
    public void FindComponent_falls_back_to_element_name_normalization()
    {
        var components = new Dictionary<string, string> { ["cardContainer"] = "impl" };
        var data = new CanvasComponentRenderData(
            "js-cardcontainer", "cardcontainer", null, new Dictionary<string, JsonElement>());
        Assert.Equal("impl", CanvasRender.FindComponent(components, data));
    }

    [Fact]
    public void GetComponentRenderData_extracts_the_uuid_and_strips_it_from_props()
    {
        var node = Element("js-card", props: new()
        {
            ["canvasUuid"] = Json("\"uuid-1\""),
            ["heading"] = Json("\"Hello\""),
        });
        var data = CanvasRender.GetComponentRenderData(node)!;
        Assert.Equal("card", data.ComponentName);
        Assert.Equal("uuid-1", data.ComponentUuid);
        Assert.False(data.Props.ContainsKey("canvasUuid"));
        Assert.Equal("Hello", data.Props["heading"].GetString());
    }

    [Fact]
    public void Structural_elements_produce_no_render_data()
        => Assert.Null(CanvasRender.GetComponentRenderData(Element("drupal-markup")));
}

public class CommentMarkerTests
{
    [Theory]
    [InlineData(CanvasMarkerType.Component, CanvasMarkerPosition.Start, "uuid-1", "canvas-start-uuid-1")]
    [InlineData(CanvasMarkerType.Component, CanvasMarkerPosition.End, "uuid-1", "canvas-end-uuid-1")]
    [InlineData(CanvasMarkerType.Slot, CanvasMarkerPosition.Start, "uuid-1/body", "canvas-slot-start-uuid-1/body")]
    [InlineData(CanvasMarkerType.Slot, CanvasMarkerPosition.End, "uuid-1/body", "canvas-slot-end-uuid-1/body")]
    [InlineData(CanvasMarkerType.Region, CanvasMarkerPosition.Start, "content", "canvas-region-start-content")]
    [InlineData(CanvasMarkerType.Region, CanvasMarkerPosition.End, "content", "canvas-region-end-content")]
    public void Marker_text_matches_the_preview_geometry_contract(
        CanvasMarkerType type, CanvasMarkerPosition position, string id, string expected)
        => Assert.Equal(expected, CommentMarkers.Format(type, position, id));

    [Fact]
    public void Full_comments_wrap_the_marker_with_spaces()
        => Assert.Equal(
            "<!-- canvas-start-uuid-1 -->",
            CommentMarkers.FormatComment(
                CanvasMarkerType.Component, CanvasMarkerPosition.Start, "uuid-1"));
}

public class PageJsonTests
{
    [Fact]
    public void Slot_unions_round_trip_through_json()
    {
        const string json =
            """
            {"element":"js-card","props":{"heading":"Hi","count":3},"slots":{"body":[{"element":"js-text"},"<p>markup</p>",[{"element":"js-nested"}]],"single":"<em>one</em>"}}
            """;
        var element = JsonSerializer.Deserialize<CanvasComponentTreeElement>(json, CanvasJson.Options)!;

        var body = Assert.IsType<CanvasSlot.Group>(element.Slots!["body"]);
        Assert.Equal(3, body.Children.Count);
        Assert.IsType<CanvasSlot.Node>(body.Children[0]);
        Assert.IsType<CanvasSlot.Markup>(body.Children[1]);
        Assert.IsType<CanvasSlot.Group>(body.Children[2]);
        Assert.IsType<CanvasSlot.Markup>(element.Slots["single"]);

        var serialized = JsonSerializer.Serialize(element, CanvasJson.Options);
        Assert.Equal(
            JsonDocument.Parse(json).RootElement.ToString(),
            JsonDocument.Parse(serialized).RootElement.ToString());
    }

    [Fact]
    public void CanvasDraftMode_is_only_written_when_set()
    {
        var element = new CanvasComponentTreeElement { Element = "canvas-page" };
        Assert.DoesNotContain("canvasDraftMode", JsonSerializer.Serialize(element, CanvasJson.Options));
        var draft = element with { CanvasDraftMode = true };
        Assert.Contains("\"canvasDraftMode\":true", JsonSerializer.Serialize(draft, CanvasJson.Options));
    }

    [Fact]
    public void PageResult_parse_distinguishes_redirects_by_shape()
    {
        Assert.IsType<PageRedirect>(PageResult.Parse(
            """{"redirect":{"external":true,"url":"https://x.example","statusCode":302}}"""));
        Assert.IsType<Page>(PageResult.Parse(
            """
            {"content":null,"head":{"title":"T"},"route":{"name":"n","requestUri":"/","params":{},"managedByCanvas":false,"entity":null}}
            """));
    }
}
