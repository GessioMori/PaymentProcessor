namespace PaymentProcessor.Api.Entities;

public record PaymentRecord(Guid CorrelationId, double Amount, DateTime RequestedAt);