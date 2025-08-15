namespace PaymentProcessor.Api.Entities;

public record PaymentRecord(Guid CorrelationId, double Amount, double RequestedAt, string ServiceType);