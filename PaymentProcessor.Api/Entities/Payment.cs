namespace PaymentProcessor.Api.Entities;

public record Payment(Guid CorrelationId, double Amount);