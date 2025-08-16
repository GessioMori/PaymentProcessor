namespace PaymentProcessor.Shared;
public record PaymentRecord(Guid CorrelationId, double Amount, string RequestedAt);