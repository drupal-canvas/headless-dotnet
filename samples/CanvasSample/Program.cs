using CanvasSample.Components;
using DrupalCanvas.Headless;
using DrupalCanvas.Headless.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

// Loads CANVAS_SITE_URL (and friends) from .env; see .env.example. Real
// environment variables win over the file.
builder.Configuration.AddDotEnvFile();

builder.Services.AddRazorComponents();
builder.Services.AddDrupalCanvasHeadless(options =>
    options.BaseUrl = builder.Configuration["CANVAS_SITE_URL"]);
builder.Services.AddDrupalCanvasComponents(components =>
    components.AddFromAssembly(typeof(Program).Assembly, builder.Environment.ContentRootPath));

var app = builder.Build();

app.UseStaticFiles();
app.UseDrupalCanvasFrameAncestors();
app.MapDrupalCanvasHeadless();

// Catch-all Drupal page rendering: resolve the requested path through the
// Canvas content API and render the answer with the Blazor component tree.
app.MapFallback(async (HttpContext context, DraftServer server) =>
{
    var path = (context.Request.Path.Value ?? "/") + context.Request.QueryString;
    var result = await server.FetchPageAsync(path);

    if (result is PageRedirect redirect)
    {
        context.Response.StatusCode = redirect.Redirect.StatusCode;
        context.Response.Headers.Location = redirect.Redirect.Url;
        return Results.Empty;
    }

    var page = result as Page;
    if (page is null)
    {
        context.Response.StatusCode = 404;
    }
    return new RazorComponentResult<CanvasPage>(new Dictionary<string, object?>
    {
        [nameof(CanvasPage.Page)] = page,
        [nameof(CanvasPage.Path)] = path,
    });
});

app.Run();
