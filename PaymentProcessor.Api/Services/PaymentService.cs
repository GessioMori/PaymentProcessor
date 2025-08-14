using PaymentProcessor.Api.Entities;

namespace PaymentProcessor.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly HttpClient httpClient;

    public PaymentService()
    {
        this.httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://payment-processor-default:8080")
        };
    }

    public async Task<bool> ProcessPaymentAsync(Payment payment)
    {
        PaymentRecord paymentRecord = new(
            payment.CorrelationId,
            payment.Amount,
            DateTime.UtcNow
        );

        HttpResponseMessage response = await this.httpClient.PostAsJsonAsync("/payments", paymentRecord);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        return false;
    }
}