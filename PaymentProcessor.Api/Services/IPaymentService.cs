using PaymentProcessor.Api.Entities;

namespace PaymentProcessor.Api.Services;
public interface IPaymentService
{
    void ProcessPaymentAsync(Payment payment);
    Task<StatsResponse> GetSummaryAsync(DateTime from, DateTime to);
}