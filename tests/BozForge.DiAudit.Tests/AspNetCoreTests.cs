using Microsoft.AspNetCore.Mvc.Testing;
using SampleApp;

namespace DiAuditTests;

public class AspNetCoreTests
{
    private static string[] Names(IEnumerable<DiRegistration> list) => list.Select(r => r.ToString()).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Finds_unused_and_shadowed_registrations_of_a_real_application()
    {
        using var factory = new WebApplicationFactory<Program>();

        var report = factory.AuditServices().Run();

        // IAuditSink (resolved inside a factory) and IManual (resolved by hand) are the expected false alarms.
        Assert.Equal(
            [
                "IAuditSink -> FileAuditSink (Singleton)",
                "ICache -> (factory) (Singleton)",
                "IManual -> ManualThing (Transient)",
                "IPayment[cash] -> CashPayment (Singleton)",
                "IReportExporter -> PdfReportExporter (Scoped)",
            ],
            Names(report.Unused));
        Assert.Equal(["ITaxRule -> OldTaxRule (Scoped)"], Names(report.Shadowed));
        Assert.Equal(["IAudit -> (factory) (Scoped)"], Names(report.OpaqueFactories));
        Assert.Equal([typeof(LocatorUser)], report.ServiceLocatorUsers);
    }

    [Fact]
    public void Roots_and_ignores_clear_the_report()
    {
        using var factory = new WebApplicationFactory<Program>();

        factory.AuditServices()
            .TreatAsRoot<IAuditSink>()
            .TreatAsRoot<IManual>()
            .Ignore(typeof(ICache), typeof(IReportExporter), typeof(IPayment))
            .Run()
            .AssertNoUnused();
    }

    [Fact]
    public void Hosted_services_are_audited_but_not_started()
    {
        using var factory = new WebApplicationFactory<Program>();
        var before = Worker.Starts;

        var report = factory.AuditServices().Run();

        Assert.Equal(before, Worker.Starts);
        Assert.DoesNotContain(report.Unused, r => r.ServiceType == typeof(IQueue) || r.ServiceType == typeof(INotifier));
    }

    [Fact]
    public void Hosted_services_start_when_asked()
    {
        using var factory = new WebApplicationFactory<Program>();
        var before = Worker.Starts;

        factory.AuditServices(startHostedServices: true).Run();

        Assert.True(Worker.Starts > before);
    }
}
