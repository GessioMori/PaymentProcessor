using Microsoft.AspNetCore.Mvc;
using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Services;

namespace PaymentProcessor.Api.Controllers;
[Route("")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        this.paymentService = paymentService;
    }

    [HttpPost("payments")]
    public IActionResult ProcessPayment([FromBody] Payment payment)
    {
        this.paymentService.ProcessPaymentAsync(payment).GetAwaiter().GetResult();
        return this.Ok();
    }
}