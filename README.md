# BozForge.DiAudit

[![NuGet](https://img.shields.io/nuget/v/BozForge.DiAudit.AspNetCore.svg)](https://www.nuget.org/packages/BozForge.DiAudit.AspNetCore)
[![CI](https://img.shields.io/github/actions/workflow/status/BozForge/di-audit/ci.yml?branch=main&label=CI)](https://github.com/BozForge/di-audit/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/BozForge/di-audit/blob/main/LICENSE)

Find dependency injection registrations nobody uses, in a unit test.

DiAudit reads the real `IServiceCollection` of your application, starts from the places where resolution begins (endpoints, controllers, hubs, hosted services) and follows constructor parameters. A registration that is never reached fails the test. So does a registration that a later one silently replaced.

This is not audit logging. It audits your service registrations.

*[Türkçe özet aşağıda.](#türkçe)*

## Quick start

```
dotnet add package BozForge.DiAudit.AspNetCore
```

```csharp
[Fact]
public void No_unused_registrations()
{
    using var factory = new WebApplicationFactory<Program>();

    factory.AuditServices().Run().AssertClean();
}
```

When it fails:

```
2 registration(s) are never requested:
  IReportExporter -> PdfReportExporter (Scoped)
  IPayment[cash] -> CashPayment (Singleton)
1 registration(s) are replaced by a later registration of the same service:
  ITaxRule -> OldTaxRule (Scoped)
If a registration is intentional, add .TreatAsRoot<T>() (resolved by hand) or .Ignore<T>() (kept on purpose).
```

It throws its own exception, so it works with xUnit, NUnit, MSTest and TUnit.

## What it checks

| Check | Meaning | Assert |
|---|---|---|
| Unused | Nothing reachable asks for the registration | `AssertNoUnused()` |
| Shadowed | A later registration of the same service replaced it and nobody asks for `IEnumerable<T>` | `AssertNoShadowed()` |
| Both | | `AssertClean()` |

`Run()` never throws. The report exposes `Unused`, `Shadowed`, `OpaqueFactories` and `ServiceLocatorUsers` if you prefer your own assertions.

## Where resolution starts

With `AuditServices()` these are roots, without any configuration:

- Minimal API handler parameters that come from the container, including `[FromKeyedServices]`
- Controllers: constructor parameters and `[FromServices]` action parameters
- SignalR hubs
- Your implementations of service types you do not own: `IHostedService`, `IConfigureOptions<T>`, `IExceptionHandler`, a library's handler interface and so on. Whoever owns the type consumes it.

From there DiAudit follows constructor parameters. It understands `IEnumerable<T>`, open generic registrations and keyed services.

Assembly scanners often register a class under its interface and under itself (`AddValidatorsFromAssembly`, Scrutor's `AsSelfWithInterfaces`). When the interface registration is used, the extra self-registration is not reported.

## When it cannot see a use

Some resolution happens in code, not in signatures. Tell DiAudit about it:

```csharp
factory.AuditServices()
    .TreatAsRoot<IJobHandler>()                    // resolved with GetRequiredService
    .TreatAsRoot(typeof(IValidator<>))             // open generic: all closed registrations
    .TreatAsRootWhere(t => t.Name.EndsWith("Job")) // by rule, service or implementation type
    .Ignore<ILegacyExporter>()                     // unused on purpose
    .Run()
    .AssertClean();
```

Known blind spots:

| Case | What happens | What to do |
|---|---|---|
| Factory registration (`sp => new X(sp.GetRequiredService<Y>())`) | `Y` looks unused. The factory is listed in the failure message as a hint | `TreatAsRoot<Y>()` |
| `GetService` / `GetRequiredService` calls | The service looks unused. Types that take `IServiceProvider` are listed as a hint | `TreatAsRoot<T>()` |
| Middleware constructor and `InvokeAsync` parameters | Not discovered | `TreatAsRoot<MyMiddleware>()` |
| `[AsParameters]` types, endpoint filters, Razor Pages | Not discovered | `TreatAsRoot<T>()` |

## Which registrations are audited

Only yours. `AuditServices()` audits the entry assembly and the assemblies that share the first part of its name: for `Acme.Api` that includes `Acme.Application` and `Acme.Infrastructure`. Add others with `IncludeAssemblyOf<T>()`. Framework and third-party registrations are never reported.

## About starting the application

`WebApplicationFactory` starts your application once, because endpoints do not exist before that. DiAudit removes hosted services from that run so background work does not start; they are still audited. Pass `startHostedServices: true` to keep them.

Code in `Program.cs` itself still runs, exactly as in your other `WebApplicationFactory` tests.

## Without ASP.NET Core

For workers and console applications use the core package and name the roots yourself:

```
dotnet add package BozForge.DiAudit
```

```csharp
var services = new ServiceCollection();
services.AddMyApplication();

DiAudit.For(services)
    .IncludeAssemblyOf<Worker>()
    .Run()
    .AssertClean();
```

Hosted services are roots here too, because `IHostedService` is not your type.

## Requirements

.NET 8 or later. `BozForge.DiAudit.AspNetCore` depends on `Microsoft.AspNetCore.Mvc.Testing`; keep that package on the same major version as your application, as usual.

## Türkçe

DiAudit, kimsenin istemediği DI kayıtlarını bir birim testinde bulur. Uygulamanın gerçek `IServiceCollection`'ını alır, çözümlemenin başladığı yerlerden (uç noktalar, controller'lar, hub'lar, arka plan servisleri) kurucu parametrelerini izler. Hiçbir yerden ulaşılmayan kayıt testi kırar; aynı servisin sonraki kaydı tarafından ezilen kayıt da.

```csharp
using var factory = new WebApplicationFactory<Program>();
factory.AuditServices().Run().AssertClean();
```

Fabrika lambdasının içinden ya da `GetService` ile elle çözülen servisler grafta görünmez; bunları `TreatAsRoot<T>()` ile, bilerek duran kayıtları `Ignore<T>()` ile bildirirsin. Yalnızca kendi derlemelerindeki kayıtlar raporlanır.

## License

MIT
