using PaymentProcessor.Api.Entities;

namespace PaymentProcessor.Api.Services;
public interface IPaymentService
{
    Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<StatsResponse> GetSummaryAsync(DateTime from, DateTime to);
}