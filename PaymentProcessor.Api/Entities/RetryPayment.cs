namespace PaymentProcessor.Api.Entities;
public record RetryPayment(Payment Payment, int Attempt = 0);