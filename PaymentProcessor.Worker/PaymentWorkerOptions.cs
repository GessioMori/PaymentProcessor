namespace PaymentProcessor.Worker;
public class PaymentWorkerOptions
{
    public int MaxConcurrency { get; set; } = 20;
    public int HttpTimeoutSeconds { get; set; } = 3;
    public int MaxRetryAttempts { get; set; } = 10;
    public int ConnectionsPerServer { get; set; } = 50;
    public int PooledConnectionLifetimeMinutes { get; set; } = 10;
    public int PooledConnectionIdleTimeoutMinutes { get; set; } = 2;
    public int ConnectTimeoutSeconds { get; set; } = 2;
}
