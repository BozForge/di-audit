using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BozForge.DiAudit;

/// <summary>
/// Configures an audit: which assemblies are yours, where resolution starts, and what to leave alone.
/// </summary>
public sealed class DiAuditBuilder
{
    private readonly List<ServiceDescriptor> _descriptors;
    private readonly HashSet<Assembly> _assemblies = new();
    private readonly List<Func<Type, bool>> _rootRules = new();
    private readonly List<Func<Type, bool>> _ignoreRules = new();
    private readonly List<(Type Type, object? Key)> _requests = new();
    private readonly List<Type> _rootTypes = new();

    internal DiAuditBuilder(IEnumerable<ServiceDescriptor> descriptors) => _descriptors = descriptors.ToList();

    /// <summary>
    /// Limits the audit to registrations whose service type lives in the given assemblies.
    /// Without this call every assembly except <c>Microsoft.*</c> and <c>System.*</c> is audited.
    /// </summary>
    public DiAuditBuilder IncludeAssembly(params Assembly[] assemblies)
    {
        foreach (var a in assemblies ?? throw new ArgumentNullException(nameof(assemblies))) _assemblies.Add(a);
        return this;
    }

    /// <summary>Limits the audit to the assembly that contains <typeparamref name="T"/>. Can be called more than once.</summary>
    public DiAuditBuilder IncludeAssemblyOf<T>() => IncludeAssembly(typeof(T).Assembly);

    /// <summary>
    /// Declares that <typeparamref name="T"/> is resolved in a way the audit cannot see
    /// (for example <c>GetRequiredService</c>, or by a library that scans for it).
    /// </summary>
    public DiAuditBuilder TreatAsRoot<T>() => TreatAsRoot(typeof(T));

    /// <summary>
    /// Declares types that are resolved in a way the audit cannot see. Every registration of a listed service type
    /// counts as used. An open generic definition (<c>typeof(IHandler&lt;&gt;)</c>) covers all its closed registrations.
    /// A type that is not registered is treated as something the container builds (its constructors are followed).
    /// </summary>
    public DiAuditBuilder TreatAsRoot(params Type[] types)
    {
        _rootTypes.AddRange(types ?? throw new ArgumentNullException(nameof(types)));
        return this;
    }

    /// <summary>
    /// Declares registrations as roots by rule. The predicate receives the service type and, when there is one,
    /// the implementation type; a match on either makes the registration a root.
    /// </summary>
    public DiAuditBuilder TreatAsRootWhere(Func<Type, bool> predicate)
    {
        _rootRules.Add(predicate ?? throw new ArgumentNullException(nameof(predicate)));
        return this;
    }

    /// <summary>Keeps registrations of <typeparamref name="T"/> out of the report. Their dependencies are not followed.</summary>
    public DiAuditBuilder Ignore<T>() => Ignore(typeof(T));

    /// <summary>Keeps registrations of the listed service types out of the report. Their dependencies are not followed.</summary>
    public DiAuditBuilder Ignore(params Type[] types)
    {
        foreach (var type in types ?? throw new ArgumentNullException(nameof(types)))
        {
            var captured = type;
            _ignoreRules.Add(t => t == captured || (t.IsGenericType && t.GetGenericTypeDefinition() == captured));
        }
        return this;
    }

    /// <summary>Keeps registrations out of the report by rule. The predicate receives the service type.</summary>
    public DiAuditBuilder IgnoreWhere(Func<Type, bool> predicate)
    {
        _ignoreRules.Add(predicate ?? throw new ArgumentNullException(nameof(predicate)));
        return this;
    }

    /// <summary>
    /// Records that something asks the container for <paramref name="serviceType"/>, the way a constructor parameter would.
    /// Integration packages use this to feed endpoint handler parameters into the audit.
    /// </summary>
    /// <param name="serviceType">The requested type; <c>IEnumerable&lt;T&gt;</c> is understood.</param>
    /// <param name="serviceKey">The key for a keyed request, otherwise <c>null</c>.</param>
    public DiAuditBuilder AddRequest(Type serviceType, object? serviceKey = null)
    {
        _requests.Add((serviceType ?? throw new ArgumentNullException(nameof(serviceType)), serviceKey));
        return this;
    }

    /// <summary>Walks the registrations and returns what was found. Does not throw; assert on the report.</summary>
    public DiAuditReport Run()
    {
        var graph = new Graph(_descriptors);

        // Registrations of a service type that is not yours (IHostedService, IConfigureOptions<T>, a library's
        // handler interface) are consumed by whoever owns that type. If the implementation is yours, start there.
        foreach (var d in _descriptors)
        {
            if (InScope(d.ServiceType)) continue;
            var impl = Graph.ImplementationTypeOf(d);
            if (impl is not null && InScope(impl)) graph.Use(d);
        }

        foreach (var d in _descriptors)
        {
            var impl = Graph.ImplementationTypeOf(d);
            if (_rootRules.Any(rule => rule(d.ServiceType) || (impl is not null && rule(impl)))) graph.Use(d);
        }

        foreach (var type in _rootTypes)
        {
            var registered = _descriptors.Where(d => Graph.Matches(d, type)).ToList();
            if (registered.Count == 0 && !type.IsGenericTypeDefinition) graph.Walk(type);
            foreach (var d in registered) graph.Use(d);
        }

        foreach (var (type, key) in _requests) graph.Request(type, key);

        var audited = _descriptors
            .Where(d => InScope(d.ServiceType) && !_ignoreRules.Any(rule => rule(d.ServiceType)))
            .ToList();

        var shadowed = audited
            .GroupBy(d => (d.ServiceType, d.ServiceKey))
            .Where(g => g.Count() > 1 && !graph.Enumerated.Contains((g.Key.ServiceType, g.Key.ServiceKey)) && graph.Used.Contains(g.Last()))
            .SelectMany(g => g.Take(g.Count() - 1))
            .Where(d => !graph.Used.Contains(d))
            .ToList();

        // Scanners (FluentValidation, Scrutor's AsSelfWithInterfaces) register a class under its interface and under
        // itself. When the interface registration is used, the extra self-registration is not worth a failing test.
        var usedImplementations = new HashSet<Type>(
            graph.Used.Select(Graph.ImplementationTypeOf).Where(t => t is not null)!);

        var unused = audited
            .Where(d => !graph.Used.Contains(d) && !shadowed.Contains(d))
            .Where(d => !(Graph.ImplementationTypeOf(d) == d.ServiceType && usedImplementations.Contains(d.ServiceType)))
            .ToList();

        // Hints are only useful for code the reader can change.
        return new DiAuditReport(
            unused.Select(d => new DiRegistration(d)).ToList(),
            shadowed.Select(d => new DiRegistration(d)).ToList(),
            graph.Factories.Where(d => InScope(d.ServiceType)).Select(d => new DiRegistration(d)).ToList(),
            graph.ServiceLocatorUsers.Where(InScope).ToList());
    }

    private bool InScope(Type type)
    {
        var assembly = type.Assembly;
        if (_assemblies.Count > 0) return _assemblies.Contains(assembly);

        var name = assembly.GetName().Name ?? "";
        return !(name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                 name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "System" or "mscorlib" or "netstandard");
    }
}
