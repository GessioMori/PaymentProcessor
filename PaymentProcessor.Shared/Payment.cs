namespace PaymentProcessor.Shared;
public record Payment(Guid CorrelationId, double Amount);