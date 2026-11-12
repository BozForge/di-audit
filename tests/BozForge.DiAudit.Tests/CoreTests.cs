using Microsoft.Extensions.DependencyInjection;

namespace DiAuditTests;

public class CoreTests
{
    private interface IA { }
    private class A(IB b) : IA { public IB B => b; }
    private interface IB { }
    private class B : IB { }
    private interface IDead { }
    private class Dead : IDead { }
    private class Root(IA a) { public IA A => a; }

    private interface IRule { }
    private class Rule1 : IRule { }
    private class Rule2 : IRule { }
    private class WantsOne(IRule rule) { public IRule Rule => rule; }
    private class WantsAll(IEnumerable<IRule> rules) { public IEnumerable<IRule> Rules => rules; }

    private interface IRepo<T> { }
    private class Repo<T> : IRepo<T> { }
    private class WantsRepo(IRepo<int> repo) { public IRepo<int> Repo => repo; }

    private interface IPay { }
    private class Card : IPay { }
    private class Cash : IPay { }
    private class WantsCard([FromKeyedServices("card")] IPay pay) { public IPay Pay => pay; }

    private class Sorter : IComparer<string> { public Sorter(IB b) { } public int Compare(string? x, string? y) => 0; }
    private class Locator(IServiceProvider sp) { public IServiceProvider Sp => sp; }

    private static string[] Names(IEnumerable<DiRegistration> list) => list.Select(r => r.ToString()).ToArray();

    [Fact]
    public void Follows_constructor_chain_and_reports_the_rest()
    {
        var services = new ServiceCollection()
            .AddScoped<IA, A>()
            .AddScoped<IB, B>()
            .AddScoped<IDead, Dead>();

        var report = DiAudit.For(services).TreatAsRoot<Root>().Run();

        Assert.Equal(["IDead -> Dead (Scoped)"], Names(report.Unused));
        Assert.Empty(report.Shadowed);
    }

    [Fact]
    public void Without_roots_everything_is_unused()
    {
        var services = new ServiceCollection().AddScoped<IA, A>().AddScoped<IB, B>();

        Assert.Equal(2, DiAudit.For(services).Run().Unused.Count);
    }

    [Fact]
    public void Earlier_registration_is_shadowed_when_a_single_instance_is_requested()
    {
        var services = new ServiceCollection().AddScoped<IRule, Rule1>().AddScoped<IRule, Rule2>();

        var report = DiAudit.For(services).TreatAsRoot<WantsOne>().Run();

        Assert.Equal(["IRule -> Rule1 (Scoped)"], Names(report.Shadowed));
        Assert.Empty(report.Unused);
        Assert.Throws<DiAuditException>(() => report.AssertNoShadowed());
        report.AssertNoUnused();
    }

    [Fact]
    public void Enumerable_request_uses_every_registration()
    {
        var services = new ServiceCollection().AddScoped<IRule, Rule1>().AddScoped<IRule, Rule2>();

        DiAudit.For(services).TreatAsRoot<WantsAll>().Run().AssertClean();
    }

    [Fact]
    public void Open_generic_registration_is_used_by_a_closed_request()
    {
        var services = new ServiceCollection().AddScoped(typeof(IRepo<>), typeof(Repo<>));

        DiAudit.For(services).TreatAsRoot<WantsRepo>().Run().AssertClean();
        Assert.Equal(["IRepo<> -> Repo<> (Scoped)"], Names(DiAudit.For(services).Run().Unused));
    }

    [Fact]
    public void Keyed_registrations_are_matched_by_key()
    {
        var services = new ServiceCollection()
            .AddKeyedSingleton<IPay, Card>("card")
            .AddKeyedSingleton<IPay, Cash>("cash");

        var report = DiAudit.For(services).TreatAsRoot<WantsCard>().Run();

        Assert.Equal(["IPay[cash] -> Cash (Singleton)"], Names(report.Unused));
    }

    [Fact]
    public void Implementation_of_a_foreign_service_type_is_a_root()
    {
        // IComparer<string> belongs to the framework: whoever owns the type consumes it.
        var services = new ServiceCollection()
            .AddSingleton<IComparer<string>, Sorter>()
            .AddScoped<IB, B>();

        DiAudit.For(services).Run().AssertClean();
    }

    [Fact]
    public void TreatAsRoot_on_a_registered_service_marks_it_and_its_dependencies_used()
    {
        var services = new ServiceCollection().AddScoped<IA, A>().AddScoped<IB, B>().AddScoped<IDead, Dead>();

        var report = DiAudit.For(services).TreatAsRoot<IA>().Run();

        Assert.Equal(["IDead -> Dead (Scoped)"], Names(report.Unused));
    }

    [Fact]
    public void TreatAsRoot_with_an_open_generic_covers_closed_registrations()
    {
        var services = new ServiceCollection().AddScoped<IRepo<int>, Repo<int>>().AddScoped<IRepo<string>, Repo<string>>();

        DiAudit.For(services).TreatAsRoot(typeof(IRepo<>)).Run().AssertClean();
    }

    [Fact]
    public void TreatAsRootWhere_matches_service_or_implementation()
    {
        var services = new ServiceCollection().AddScoped<IA, A>().AddScoped<IB, B>().AddScoped<IDead, Dead>();

        var report = DiAudit.For(services).TreatAsRootWhere(t => t == typeof(A)).Run();

        Assert.Equal(["IDead -> Dead (Scoped)"], Names(report.Unused));
    }

    [Fact]
    public void Ignore_keeps_a_registration_out_of_the_report()
    {
        var services = new ServiceCollection().AddScoped<IDead, Dead>().AddScoped<IB, B>();

        var report = DiAudit.For(services).Ignore<IDead>().IgnoreWhere(t => t == typeof(IB)).Run();

        report.AssertClean();
    }

    [Fact]
    public void Factory_dependencies_are_not_seen_and_the_message_says_so()
    {
        var services = new ServiceCollection()
            .AddScoped<IA>(sp => new A(sp.GetRequiredService<IB>()))
            .AddScoped<IB, B>();

        var report = DiAudit.For(services).TreatAsRoot<Root>().Run();

        Assert.Equal(["IB -> B (Scoped)"], Names(report.Unused));
        Assert.Equal(["IA -> (factory) (Scoped)"], Names(report.OpaqueFactories));
        var error = Assert.Throws<DiAuditException>(() => report.AssertNoUnused());
        Assert.Contains("factory for IA", error.Message);
        Assert.Contains("TreatAsRoot<T>()", error.Message);
    }

    [Fact]
    public void Types_taking_IServiceProvider_are_listed_as_hints()
    {
        var services = new ServiceCollection().AddScoped<Locator>().AddScoped<IB, B>();

        var report = DiAudit.For(services).TreatAsRoot<Locator>().Run();

        Assert.Equal([typeof(Locator)], report.ServiceLocatorUsers);
        Assert.Equal(["IB -> B (Scoped)"], Names(report.Unused));
    }

    private class OpenSorter<T>(IB b, IRepo<T> repo) : IComparer<T>
    {
        public IB B => b;
        public IRepo<T> Repo => repo;
        public int Compare(T? x, T? y) => 0;
    }

    [Fact]
    public void Open_generic_implementation_has_its_constructor_followed()
    {
        // The shape of a MediatR pipeline behaviour: foreign open service type, own open implementation.
        var services = new ServiceCollection()
            .AddScoped(typeof(IComparer<>), typeof(OpenSorter<>))
            .AddScoped<IB, B>()
            .AddScoped(typeof(IRepo<>), typeof(Repo<>))
            .AddScoped<IDead, Dead>();

        var report = DiAudit.For(services).Run();

        Assert.Equal(["IDead -> Dead (Scoped)"], Names(report.Unused));
    }

    [Fact]
    public void Self_registration_next_to_a_used_interface_registration_is_not_reported()
    {
        // What assembly scanners do: AddScoped<IB, B>() and AddScoped<B>().
        var services = new ServiceCollection().AddScoped<IA, A>().AddScoped<IB, B>().AddScoped<B>();

        DiAudit.For(services).TreatAsRoot<Root>().Run().AssertClean();
        Assert.Equal(3, DiAudit.For(services).Run().Unused.Count);
    }

    [Fact]
    public void IncludeAssembly_limits_what_is_audited()
    {
        var services = new ServiceCollection().AddScoped<IDead, Dead>();

        DiAudit.For(services).IncludeAssemblyOf<DiAuditReport>().Run().AssertClean();
        Assert.Single(DiAudit.For(services).IncludeAssemblyOf<CoreTests>().Run().Unused);
    }

    [Fact]
    public void Self_registered_concrete_type_and_instance_are_reported_readably()
    {
        var services = new ServiceCollection().AddTransient<Dead>().AddSingleton<IB>(new B());

        var report = DiAudit.For(services).Run();

        Assert.Equal(["Dead (Transient)", "IB -> (instance) (Singleton)"], Names(report.Unused));
    }
}
