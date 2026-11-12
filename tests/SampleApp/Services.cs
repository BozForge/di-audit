namespace SampleApp;

public interface IClock { DateTime Now { get; } }
public class SystemClock : IClock { public DateTime Now => DateTime.UtcNow; }

public interface ITaxRule { decimal Rate { get; } }
public class OldTaxRule : ITaxRule { public decimal Rate => 0.18m; }
public class NewTaxRule : ITaxRule { public decimal Rate => 0.20m; }

public interface IGreeter { string Greet(string name); }
public class Greeter(IClock clock, ITaxRule tax) : IGreeter
{
    public string Greet(string name) => $"{name} {clock.Now:O} {tax.Rate}";
}

public interface IReportExporter { }
public class PdfReportExporter : IReportExporter { }

public interface IOrderRepo { }
public class OrderRepo : IOrderRepo { }

public interface IQueue { }
public class MemoryQueue : IQueue { }

public interface INotifier { }
public class EmailNotifier : INotifier { }
public class SmsNotifier : INotifier { }

public interface IRepository<T> { }
public class Repository<T> : IRepository<T> { }
public record Order(int Id);
public record Invoice(int Id);

public interface IPayment { }
public class CardPayment : IPayment { }
public class CashPayment : IPayment { }

public interface IAuditSink { }
public class FileAuditSink : IAuditSink { }
public interface IAudit { }
public class Audit(IAuditSink sink) : IAudit { public IAuditSink Sink => sink; }

public interface ICache { }
public class Cache : ICache { }

public interface IManual { }
public class ManualThing : IManual { }

public class Worker(IQueue queue, IEnumerable<INotifier> notifiers) : BackgroundService
{
    public static int Starts;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref Starts);
        _ = queue; _ = notifiers;
        return Task.CompletedTask;
    }
}

public static class Handlers
{
    public static string Pay(int id, [FromKeyedServices("card")] IPayment payment) => $"{id} {payment.GetType().Name}";
}

public interface IPresence { }
public class Presence : IPresence { }
public class ChatHub(IPresence presence) : Microsoft.AspNetCore.SignalR.Hub
{
    public IPresence Presence => presence;
}

public interface ILocatorUser { }
public class LocatorUser(IServiceProvider provider) : ILocatorUser
{
    public object? Manual => provider.GetService(typeof(IManual));
}
