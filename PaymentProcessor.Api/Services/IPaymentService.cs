using PaymentProcessor.Api.Entities;

namespace PaymentProcessor.Api.Services;
public interface IPaymentService
{
    Task<bool> ProcessPaymentAsync(Payment payment);
}