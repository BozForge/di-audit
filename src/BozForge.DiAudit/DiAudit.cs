using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace BozForge.DiAudit;

/// <summary>
/// Entry point. Audits an <see cref="IServiceCollection"/> for registrations nobody asks for.
/// </summary>
public static class DiAudit
{
    /// <summary>
    /// Starts an audit over the given service collection. The collection is copied; later changes are not seen.
    /// </summary>
    /// <param name="services">The service collection of the application under test.</param>
    public static DiAuditBuilder For(IEnumerable<ServiceDescriptor> services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        return new DiAuditBuilder(services);
    }
}
