using System.Reflection;
using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// Maps Canvas component machine names (component.yml <c>machineName</c>) to
/// the Blazor component types implementing them — the .NET counterpart of the
/// JavaScript adapters' generated component registry module.
/// </summary>
public interface ICanvasComponentRegistry
{
    IReadOnlyDictionary<string, Type> Components { get; }

    /// <summary>Resolves a component implementation for one rendered tree node.</summary>
    Type? Resolve(CanvasComponentRenderData data);
}

public sealed class CanvasComponentRegistry(IReadOnlyDictionary<string, Type> components)
    : ICanvasComponentRegistry
{
    public IReadOnlyDictionary<string, Type> Components { get; } = components;

    public Type? Resolve(CanvasComponentRenderData data)
        => CanvasRender.FindComponent(Components, data);
}

public sealed class CanvasComponentRegistryBuilder
{
    private readonly Dictionary<string, Type> _components = [];

    /// <summary>Registers one component implementation under its machine name.</summary>
    public CanvasComponentRegistryBuilder Add(string machineName, Type componentType)
    {
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"{componentType} does not implement IComponent.", nameof(componentType));
        }
        _components[machineName] = componentType;
        return this;
    }

    public CanvasComponentRegistryBuilder Add<TComponent>(string machineName)
        where TComponent : IComponent
        => Add(machineName, typeof(TComponent));

    /// <summary>
    /// Registers every Blazor component in the assembly whose type name
    /// matches a machine name under Canvas's element-name normalization
    /// (case, and the <c>._:-</c> separators, are ignored): a component.yml
    /// with <c>machineName: card_container</c> binds to a component class
    /// named <c>CardContainer</c>. An explicit <see cref="Add(string, Type)"/>
    /// wins over a convention match.
    /// </summary>
    public CanvasComponentRegistryBuilder AddFromAssembly(Assembly assembly, string projectRoot)
    {
        var componentTypes = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                && typeof(IComponent).IsAssignableFrom(type))
            .ToLookup(type => Normalize(type.Name));

        var payload = ComponentDiscovery.BuildPayload(projectRoot);
        foreach (var component in payload.Components)
        {
            if (_components.ContainsKey(component.MachineName))
            {
                continue;
            }
            var match = componentTypes[Normalize(component.MachineName)].FirstOrDefault();
            if (match is not null)
            {
                _components[component.MachineName] = match;
            }
        }
        return this;
    }

    private static string Normalize(string name)
        => new string(name.Where(c => c is not ('.' or ':' or '_' or '-')).ToArray())
            .ToLowerInvariant();

    public ICanvasComponentRegistry Build() => new CanvasComponentRegistry(_components);
}

public static class CanvasComponentRegistryServiceCollectionExtensions
{
    /// <summary>Registers the app's Canvas component implementations.</summary>
    public static IServiceCollection AddDrupalCanvasComponents(
        this IServiceCollection services,
        Action<CanvasComponentRegistryBuilder> configure)
    {
        var builder = new CanvasComponentRegistryBuilder();
        configure(builder);
        services.AddSingleton(builder.Build());
        return services;
    }
}
