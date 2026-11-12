using System;
using System.Collections.Generic;
using System.Text;

namespace BozForge.DiAudit;

/// <summary>The result of an audit.</summary>
public sealed class DiAuditReport
{
    internal DiAuditReport(
        IReadOnlyList<DiRegistration> unused,
        IReadOnlyList<DiRegistration> shadowed,
        IReadOnlyList<DiRegistration> opaqueFactories,
        IReadOnlyList<Type> serviceLocatorUsers)
    {
        Unused = unused;
        Shadowed = shadowed;
        OpaqueFactories = opaqueFactories;
        ServiceLocatorUsers = serviceLocatorUsers;
    }

    /// <summary>Registrations nothing reachable asks for.</summary>
    public IReadOnlyList<DiRegistration> Unused { get; }

    /// <summary>
    /// Registrations replaced by a later registration of the same service, where nobody asks for
    /// <c>IEnumerable&lt;T&gt;</c>. The container never hands these out.
    /// </summary>
    public IReadOnlyList<DiRegistration> Shadowed { get; }

    /// <summary>
    /// Reachable factory registrations. What a factory resolves inside its delegate cannot be seen,
    /// so a service used only there shows up in <see cref="Unused"/>.
    /// </summary>
    public IReadOnlyList<DiRegistration> OpaqueFactories { get; }

    /// <summary>
    /// Reachable types that take <c>IServiceProvider</c> or <c>IServiceScopeFactory</c>.
    /// What they resolve by hand cannot be seen either.
    /// </summary>
    public IReadOnlyList<Type> ServiceLocatorUsers { get; }

    /// <summary>Throws <see cref="DiAuditException"/> when there are unused registrations.</summary>
    public DiAuditReport AssertNoUnused()
    {
        if (Unused.Count > 0) throw new DiAuditException(Describe(unused: true, shadowed: false));
        return this;
    }

    /// <summary>Throws <see cref="DiAuditException"/> when there are shadowed registrations.</summary>
    public DiAuditReport AssertNoShadowed()
    {
        if (Shadowed.Count > 0) throw new DiAuditException(Describe(unused: false, shadowed: true));
        return this;
    }

    /// <summary>Throws <see cref="DiAuditException"/> when there are unused or shadowed registrations.</summary>
    public DiAuditReport AssertClean()
    {
        if (Unused.Count > 0 || Shadowed.Count > 0) throw new DiAuditException(Describe(unused: true, shadowed: true));
        return this;
    }

    /// <summary>The full report as text.</summary>
    public override string ToString() =>
        Unused.Count == 0 && Shadowed.Count == 0
            ? "DiAudit: no unused or shadowed registrations."
            : Describe(unused: true, shadowed: true);

    private string Describe(bool unused, bool shadowed)
    {
        var sb = new StringBuilder();

        if (unused && Unused.Count > 0)
        {
            sb.AppendLine($"{Unused.Count} registration(s) are never requested:");
            foreach (var r in Unused) sb.AppendLine($"  {r}");
        }

        if (shadowed && Shadowed.Count > 0)
        {
            sb.AppendLine($"{Shadowed.Count} registration(s) are replaced by a later registration of the same service:");
            foreach (var r in Shadowed) sb.AppendLine($"  {r}");
        }

        if (unused && Unused.Count > 0 && (OpaqueFactories.Count > 0 || ServiceLocatorUsers.Count > 0))
        {
            sb.AppendLine("These resolve services in a way the audit cannot see, and may explain a false alarm:");
            foreach (var r in OpaqueFactories) sb.AppendLine($"  factory for {r}");
            foreach (var t in ServiceLocatorUsers) sb.AppendLine($"  {TypeNames.Of(t)} takes IServiceProvider or IServiceScopeFactory");
        }

        sb.Append("If a registration is intentional, add .TreatAsRoot<T>() (resolved by hand) or .Ignore<T>() (kept on purpose).");
        return sb.ToString();
    }
}

/// <summary>Thrown by the assert methods of <see cref="DiAuditReport"/>.</summary>
public sealed class DiAuditException : Exception
{
    /// <summary>Creates the exception with the report text as its message.</summary>
    public DiAuditException(string message) : base(message) { }
}
