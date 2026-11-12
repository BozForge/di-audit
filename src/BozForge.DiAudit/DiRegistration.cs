using System;
using Microsoft.Extensions.DependencyInjection;

namespace BozForge.DiAudit;

/// <summary>How a registration produces its instance.</summary>
public enum DiRegistrationKind
{
    /// <summary>Registered with an implementation type.</summary>
    Type,

    /// <summary>Registered with a factory delegate. Its dependencies cannot be seen.</summary>
    Factory,

    /// <summary>Registered with a ready instance.</summary>
    Instance,
}

/// <summary>A single service registration, as reported by the audit.</summary>
public sealed class DiRegistration
{
    internal DiRegistration(ServiceDescriptor descriptor)
    {
        ServiceType = descriptor.ServiceType;
        ServiceKey = descriptor.ServiceKey;
        Lifetime = descriptor.Lifetime;
        ImplementationType = Graph.ImplementationTypeOf(descriptor);
        Kind = Graph.IsFactory(descriptor) ? DiRegistrationKind.Factory
            : ImplementationType is null ? DiRegistrationKind.Instance
            : DiRegistrationKind.Type;
    }

    /// <summary>The registered service type. Open generic registrations report the generic definition.</summary>
    public Type ServiceType { get; }

    /// <summary>The service key, or <c>null</c> for a non-keyed registration.</summary>
    public object? ServiceKey { get; }

    /// <summary>The implementation type, or <c>null</c> for factory and instance registrations.</summary>
    public Type? ImplementationType { get; }

    /// <summary>The registered lifetime.</summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>How the registration produces its instance.</summary>
    public DiRegistrationKind Kind { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var service = TypeNames.Of(ServiceType) + (ServiceKey is null ? "" : $"[{ServiceKey}]");
        var target = Kind switch
        {
            DiRegistrationKind.Factory => "(factory)",
            DiRegistrationKind.Instance => "(instance)",
            _ => TypeNames.Of(ImplementationType!),
        };
        return target == service ? $"{service} ({Lifetime})" : $"{service} -> {target} ({Lifetime})";
    }
}
