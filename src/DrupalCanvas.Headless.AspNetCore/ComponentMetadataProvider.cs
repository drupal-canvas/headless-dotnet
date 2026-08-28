using DrupalCanvas.Headless;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>Provides the component metadata payload the components endpoint serves.</summary>
public interface ICanvasComponentMetadataProvider
{
    ValueTask<ComponentMetadataPayload> GetPayloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The default provider: runs component discovery over the configured project
/// root (the host content root unless overridden).
///
/// The JavaScript adapters split this into a live development scan and a
/// build-time manifest, because their bundled server outputs strand the
/// component sources. A .NET publish has no such constraint — the
/// component.yml files ship as content items — so one code path serves both
/// modes: development scans on every request (a newly added component is
/// visible on the next fetch), production scans once and caches.
/// </summary>
public sealed class ContentRootComponentMetadataProvider(
    IHostEnvironment environment,
    IOptions<CanvasHeadlessOptions> options) : ICanvasComponentMetadataProvider
{
    private ComponentMetadataPayload? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async ValueTask<ComponentMetadataPayload> GetPayloadAsync(
        CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment())
        {
            return Build();
        }

        if (_cached is { } cached)
        {
            return cached;
        }
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _cached ??= Build();
        }
        finally
        {
            _lock.Release();
        }
    }

    private ComponentMetadataPayload Build()
        => ComponentDiscovery.BuildPayload(options.Value.ProjectRoot ?? environment.ContentRootPath);
}
