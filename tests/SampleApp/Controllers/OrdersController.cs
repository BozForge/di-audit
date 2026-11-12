using Microsoft.AspNetCore.Mvc;

namespace SampleApp.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController(IOrderRepo repo) : ControllerBase
{
    [HttpGet]
    public string Get([FromServices] IRepository<Invoice> invoices) => $"{repo.GetType().Name} {invoices.GetType().Name}";
}
