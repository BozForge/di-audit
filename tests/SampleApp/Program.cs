using SampleApp;

var builder = WebApplication.CreateBuilder(args);
var s = builder.Services;

s.AddControllers();
s.AddSingleton<IClock, SystemClock>();
s.AddScoped<ITaxRule, OldTaxRule>();                 // ezilen kayıt
s.AddScoped<ITaxRule, NewTaxRule>();
s.AddScoped<IGreeter, Greeter>();
s.AddScoped<IReportExporter, PdfReportExporter>();   // kullanılmıyor
s.AddScoped<IOrderRepo, OrderRepo>();
s.AddSingleton<IQueue, MemoryQueue>();
s.AddSingleton<INotifier, EmailNotifier>();
s.AddSingleton<INotifier, SmsNotifier>();
s.AddScoped(typeof(IRepository<>), typeof(Repository<>));
s.AddKeyedSingleton<IPayment, CardPayment>("card");
s.AddKeyedSingleton<IPayment, CashPayment>("cash");  // kullanılmıyor
s.AddSingleton<IAuditSink, FileAuditSink>();         // yalnızca fabrikanın içinden çözülüyor
s.AddScoped<IAudit>(sp => new Audit(sp.GetRequiredService<IAuditSink>()));
s.AddSingleton<ICache>(_ => new Cache());            // kullanılmıyor (fabrika)
s.AddTransient<IManual, ManualThing>();              // yalnızca GetService ile elle çözülüyor
s.AddScoped<IPresence, Presence>();
s.AddScoped<ILocatorUser, LocatorUser>();
s.AddSignalR();
s.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/hello/{name}", (string name, IGreeter greeter, IAudit audit, ILocatorUser locator) => greeter.Greet(name));
app.MapGet("/orders/{id:int}", (int id, IRepository<Order> repo, CancellationToken ct, HttpContext ctx) =>
{
    _ = ctx.RequestServices.GetService<IManual>();
    return id;
});
app.MapPost("/pay/{id:int}", Handlers.Pay);
app.MapControllers();
app.MapHub<ChatHub>("/chat");

app.Run();

public partial class Program { }
