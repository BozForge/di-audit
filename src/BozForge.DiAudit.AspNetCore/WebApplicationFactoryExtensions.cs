using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BozForge.DiAudit;

/// <summary>Starts an audit from a <see cref="WebApplicationFactory{TEntryPoint}"/>.</summary>
public static class WebApplicationFactoryExtensions
{
    /// <summary>
    /// Takes the real service collection of the application and uses its endpoints as roots:
    /// minimal API handler parameters, controllers (constructor and action parameters) and SignalR hubs.
    /// The audit is limited to the entry assembly and the assemblies that share the first part of its name
    /// (<c>Acme.Api</c> also covers <c>Acme.Application</c>); call <c>IncludeAssemblyOf</c> to add others.
    /// </summary>
    /// <remarks>
    /// The application is started once so that its endpoints exist. Hosted services are left out of that run
    /// unless <paramref name="startHostedServices"/> is set; they are still audited.
    /// </remarks>
    /// <param name="factory">The factory used by the test project.</param>
    /// <param name="startHostedServices">Start the application's own hosted services during the audit run.</param>
    public static DiAuditBuilder AuditServices<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        bool startHostedServices = false)
        where TEntryPoint : class
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        List<ServiceDescriptor>? snapshot = null;

        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            snapshot = services.ToList();
            if (startHostedServices) return;

            for (var i = services.Count - 1; i >= 0; i--)
            {
                var d = services[i];
                if (d.ServiceType == typeof(IHostedService) && !IsFrameworkHostedService(d)) services.RemoveAt(i);
            }
        }));

        var provider = app.Services; // builds and starts the host
        if (snapshot is null)
            throw new InvalidOperationException("DiAudit could not capture the service collection of the application.");

        var audit = DiAudit.For(snapshot);
        audit.IncludeAssembly(OwnAssemblies(typeof(TEntryPoint).Assembly, snapshot));

        var isService = provider.GetRequiredService<IServiceProviderIsService>();
        var isKeyedService = isService as IServiceProviderIsKeyedService;

        foreach (var endpoint in provider.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints))
        {
            var hub = endpoint.Metadata.GetMetadata<HubMetadata>();
            if (hub is not null) audit.TreatAsRoot(hub.HubType);

            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is not null) audit.TreatAsRoot(action.ControllerTypeInfo.AsType());

            var handler = action?.MethodInfo ?? endpoint.Metadata.GetMetadata<MethodInfo>();
            if (handler is null) continue;

            foreach (var parameter in handler.GetParameters())
            {
                var key = parameter.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key;
                var fromContainer = key is not null
                    ? isKeyedService?.IsKeyedService(parameter.ParameterType, key) == true
                    : isService.IsService(parameter.ParameterType);

                if (fromContainer) audit.AddRequest(parameter.ParameterType, key);
            }
        }

        return audit;
    }

    private static bool IsFrameworkHostedService(ServiceDescriptor descriptor)
    {
        var name = descriptor.ImplementationType?.Assembly.GetName().Name;
        return name is not null && name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static Assembly[] OwnAssemblies(Assembly entry, List<ServiceDescriptor> descriptors)
    {
        var own = new HashSet<Assembly> { entry };
        var entryName = entry.GetName().Name ?? "";
        var dot = entryName.IndexOf('.');
        if (dot <= 0) return own.ToArray();

        var prefix = entryName.Substring(0, dot + 1);
        foreach (var d in descriptors)
        {
            Consider(d.ServiceType);
            var impl = d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;
            if (impl is not null) Consider(impl);
        }
        return own.ToArray();

        void Consider(Type type)
        {
            var name = type.Assembly.GetName().Name;
            if (name is not null && name.StartsWith(prefix, StringComparison.Ordinal)) own.Add(type.Assembly);
        }
    }
}
