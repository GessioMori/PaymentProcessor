namespace PaymentProcessor.Api.Entities;

public record StatsResponse(RequestStats Default, RequestStats Fallback);