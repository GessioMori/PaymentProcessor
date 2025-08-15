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
    public async Task<IActionResult> ProcessPayment([FromBody] Payment payment, CancellationToken cancellationToken)
    {
        await this.paymentService.ProcessPaymentAsync(payment, cancellationToken);
        return this.Ok();
    }

    [HttpGet("payments-summary")]
    public async Task<IActionResult> ProcessPaymentSummary([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        StatsResponse summary = await this.paymentService.GetSummaryAsync(from, to);
        return this.Ok(summary);
    }
}