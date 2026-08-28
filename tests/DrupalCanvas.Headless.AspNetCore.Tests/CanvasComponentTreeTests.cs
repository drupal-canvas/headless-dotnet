using System.Text.Json;
using DrupalCanvas.Headless.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrupalCanvas.Headless.AspNetCore.Tests;

public sealed class TestCard : ComponentBase
{
    [Parameter]
    public string? Heading { get; set; }

    [Parameter]
    public int Count { get; set; }

    [Parameter]
    public MarkupString Text { get; set; }

    [Parameter]
    public RenderFragment? Body { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "card");
        builder.OpenElement(2, "h2");
        builder.AddContent(3, Heading);
        builder.CloseElement();
        builder.OpenElement(4, "span");
        builder.AddContent(5, Count);
        builder.CloseElement();
        builder.AddContent(6, Text);
        if (Body is not null)
        {
            builder.OpenElement(7, "div");
            builder.AddAttribute(8, "class", "body");
            builder.AddContent(9, Body);
            builder.CloseElement();
        }
        builder.CloseElement();
    }
}

public class CanvasComponentTreeTests
{
    private static async Task<string> RenderAsync(
        CanvasComponentTreeElement? tree,
        Action<CanvasComponentRegistryBuilder>? components = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var registryBuilder = new CanvasComponentRegistryBuilder();
        (components ?? (builder => builder.Add<TestCard>("card"))).Invoke(registryBuilder);
        services.AddSingleton(registryBuilder.Build());
        await using var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(
            provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CanvasComponentTree>(
                ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(CanvasComponentTree.Tree)] = tree }));
            return output.ToHtmlString();
        });
    }

    private static CanvasComponentTreeElement Tree(string json)
        => JsonSerializer.Deserialize<CanvasComponentTreeElement>(json, CanvasJson.Options)!;

    [Fact]
    public async Task Renders_a_public_component_without_editor_markers()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"js-card","props":{"canvasUuid":"uuid-1","heading":"Hello","count":3,"text":"<em>Rich</em>"}}
            """));

        Assert.Contains("<h2>Hello</h2>", html);
        Assert.Contains("<span>3</span>", html);
        Assert.Contains("<em>Rich</em>", html);
        Assert.DoesNotContain("canvas-start-", html);
        Assert.DoesNotContain("canvas-region-start-", html);
        Assert.DoesNotContain("canvasUuid", html);
    }

    [Fact]
    public async Task Draft_trees_carry_region_component_and_slot_markers()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"renderless-container","canvasDraftMode":true,"slots":{"default":{"element":"js-card","props":{"canvasUuid":"uuid-1","heading":"Hi"},"slots":{"body":{"element":"js-card","props":{"canvasUuid":"uuid-2","heading":"Nested"}}}}}}
            """));

        Assert.Contains("<!-- canvas-region-start-content -->", html);
        Assert.Contains("<!-- canvas-start-uuid-1 -->", html);
        Assert.Contains("<!-- canvas-slot-start-uuid-1/body -->", html);
        Assert.Contains("<!-- canvas-start-uuid-2 -->", html);
        Assert.Contains("<!-- canvas-end-uuid-1 -->", html);
        Assert.Contains("<!-- canvas-region-end-content -->", html);
        // Marker order: region wraps everything, slot markers inside the
        // component's own markup.
        Assert.True(html.IndexOf("canvas-region-start", StringComparison.Ordinal)
            < html.IndexOf("canvas-start-uuid-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_draft_tree_renders_the_region_placeholder()
    {
        var html = await RenderAsync(Tree(
            """{"element":"renderless-container","canvasDraftMode":true}"""));
        Assert.Contains("canvas--region-empty-placeholder", html);
    }

    [Fact]
    public async Task An_empty_draft_slot_renders_its_placeholder_instead_of_defaults()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"js-card","canvasDraftMode":true,"props":{"canvasUuid":"uuid-1"},"slots":{"body":"<p>default example</p>"}}
            """));

        Assert.Contains("canvas--slot-empty-placeholder", html);
        Assert.DoesNotContain("default example", html);
    }

    [Fact]
    public async Task A_public_slot_keeps_its_default_markup()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"js-card","props":{"canvasUuid":"uuid-1"},"slots":{"body":"<p>default example</p>"}}
            """));
        Assert.Contains("<p>default example</p>", html);
        Assert.DoesNotContain("canvas--slot-empty-placeholder", html);
    }

    [Fact]
    public async Task Drupal_markup_wrappers_render_their_children_transparently()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"drupal-markup","slots":{"default":["<p>lead</p>",{"element":"js-card","props":{"heading":"In markup"}}]}}
            """));
        Assert.Contains("<p>lead</p>", html);
        Assert.Contains("<h2>In markup</h2>", html);
    }

    [Fact]
    public async Task The_preview_content_region_renders_children_between_region_markers()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"js-card","canvasDraftMode":true,"props":{"canvasUuid":"chrome"},"slots":{"body":{"element":"canvas-preview-content-region","slots":{"default":{"element":"js-card","props":{"canvasUuid":"uuid-2","heading":"Routed"}}}}}}
            """));

        // The tree has an explicit content region, so no top-level region
        // markers — exactly one pair, emitted by the region element itself.
        Assert.Single(SplitOccurrences(html, "<!-- canvas-region-start-content -->"));
        Assert.Contains("<h2>Routed</h2>", html);
    }

    [Fact]
    public async Task Unregistered_components_are_omitted_with_their_subtree()
    {
        var html = await RenderAsync(Tree(
            """
            {"element":"renderless-container","slots":{"default":[{"element":"js-unknown","props":{"canvasUuid":"u"}},{"element":"js-card","props":{"heading":"Known"}}]}}
            """));
        Assert.DoesNotContain("js-unknown", html);
        Assert.Contains("<h2>Known</h2>", html);
    }

    [Fact]
    public async Task Snake_case_machine_names_match_pascal_case_components()
    {
        var html = await RenderAsync(
            Tree("""{"element":"js-test-card","props":{"heading":"Converted"}}"""),
            builder => builder.Add<TestCard>("test_card"));
        Assert.Contains("<h2>Converted</h2>", html);
    }

    private static IEnumerable<int> SplitOccurrences(string haystack, string needle)
    {
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal);
            index >= 0;
            index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            yield return index;
        }
    }
}
