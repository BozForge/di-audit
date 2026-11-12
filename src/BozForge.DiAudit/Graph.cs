using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BozForge.DiAudit;

/// <summary>Walks the registrations reachable from the requested services.</summary>
internal sealed class Graph
{
    private readonly List<ServiceDescriptor> _descriptors;
    private readonly HashSet<Type> _visitedTypes = new();

    public Graph(List<ServiceDescriptor> descriptors) => _descriptors = descriptors;

    public HashSet<ServiceDescriptor> Used { get; } = new();

    /// <summary>Service types somebody asked for as <c>IEnumerable&lt;T&gt;</c>; every registration of these counts.</summary>
    public HashSet<(Type ServiceType, object? Key)> Enumerated { get; } = new();

    /// <summary>Reachable factory registrations, in the order they were found.</summary>
    public List<ServiceDescriptor> Factories { get; } = new();

    /// <summary>Reachable types that take <see cref="IServiceProvider"/> or <see cref="IServiceScopeFactory"/>.</summary>
    public List<Type> ServiceLocatorUsers { get; } = new();

    public static Type? ImplementationTypeOf(ServiceDescriptor d) =>
        d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;

    public static bool IsFactory(ServiceDescriptor d) =>
        d.IsKeyedService ? d.KeyedImplementationFactory is not null : d.ImplementationFactory is not null;

    public static bool Matches(ServiceDescriptor d, Type serviceType) =>
        d.ServiceType == serviceType ||
        (serviceType.IsGenericTypeDefinition && d.ServiceType.IsGenericType &&
         d.ServiceType.GetGenericTypeDefinition() == serviceType);

    /// <summary>Somebody asks the container for <paramref name="type"/>.</summary>
    public void Request(Type type, object? key)
    {
        var many = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        if (many) type = type.GetGenericArguments()[0];

        var matches = _descriptors.Where(d => d.ServiceType == type && Equals(d.ServiceKey, key)).ToList();
        Type[]? genericArgs = null;

        if (type.IsConstructedGenericType && (many || matches.Count == 0))
        {
            var open = type.GetGenericTypeDefinition();
            var openMatches = _descriptors.Where(d => d.ServiceType == open && Equals(d.ServiceKey, key)).ToList();
            if (openMatches.Count > 0)
            {
                genericArgs = type.GetGenericArguments();
                matches.AddRange(openMatches);
            }
        }

        if (matches.Count == 0) return;

        if (many)
        {
            foreach (var d in matches) Enumerated.Add((d.ServiceType, key));
            foreach (var d in matches) Use(d, genericArgs);
        }
        else
        {
            // The container hands out the last registration.
            Use(matches[matches.Count - 1], genericArgs);
        }
    }

    /// <summary>The registration is consumed by something outside the graph (the framework, a manual resolve).</summary>
    public void Use(ServiceDescriptor descriptor, Type[]? genericArgs = null)
    {
        var first = Used.Add(descriptor);

        if (IsFactory(descriptor))
        {
            if (first) Factories.Add(descriptor);
            return;
        }

        var impl = ImplementationTypeOf(descriptor);
        if (impl is null) return;

        if (impl.IsGenericTypeDefinition && genericArgs is not null)
        {
            try { impl = impl.MakeGenericType(genericArgs); }
            catch (ArgumentException) { /* constraints not met: walk the open definition instead */ }
        }

        Walk(impl);
    }

    /// <summary>Follows the constructor parameters of a type that will be created by the container.</summary>
    public void Walk(Type type)
    {
        if (!_visitedTypes.Add(type)) return;

        // Every public constructor counts: which one the container picks depends on what is registered,
        // and reporting a dependency of any of them as unused would be wrong more often than right.
        foreach (var ctor in type.GetConstructors())
        {
            foreach (var p in ctor.GetParameters())
            {
                if (p.ParameterType == typeof(IServiceProvider) || p.ParameterType == typeof(IServiceScopeFactory))
                {
                    if (!ServiceLocatorUsers.Contains(type)) ServiceLocatorUsers.Add(type);
                    continue;
                }

                var key = p.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key;
                if (p.ParameterType.ContainsGenericParameters) RequestOpen(p.ParameterType, key);
                else Request(p.ParameterType, key);
            }
        }
    }

    /// <summary>
    /// A parameter of an open generic implementation (<c>IRepo&lt;T&gt;</c> inside <c>Handler&lt;T&gt;</c>):
    /// which closed type it becomes is unknown here, so every registration of the generic definition counts.
    /// </summary>
    private void RequestOpen(Type type, object? key)
    {
        if (!type.IsGenericType) return; // a bare type parameter

        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            if (!element.IsGenericType) return;
            definition = element.GetGenericTypeDefinition();
            foreach (var d in _descriptors.Where(d => Matches(d, definition) && Equals(d.ServiceKey, key)).ToList())
                Enumerated.Add((d.ServiceType, key));
        }

        foreach (var d in _descriptors.Where(d => Matches(d, definition) && Equals(d.ServiceKey, key)).ToList()) Use(d);
    }
}
