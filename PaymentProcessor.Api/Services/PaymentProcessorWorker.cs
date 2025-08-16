using Microsoft.Extensions.Options;
using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Serialization;
using PaymentProcessor.Api.Services;
using StackExchange.Redis;
using System.Threading.Channels;

public sealed class PaymentProcessorWorker : BackgroundService, IDisposable
{
    private readonly ChannelReader<Payment> externalReader;
    private readonly HttpClient httpClient;
    private readonly Uri defaultProcessor;
    private readonly Channel<RetryPayment> retryQueue;
    private readonly IDatabase redis;

    private readonly int maxConcurrency;
    private readonly SemaphoreSlim concurrencyLimiter;

    private readonly TaskFactory taskFactory;

    private long processedCount = 0;
    private long failedCount = 0;
    private long retryCount = 0;

    private readonly PaymentWorkerOptions options;

    public PaymentProcessorWorker(ChannelReader<Payment> externalReader, IDatabase redis,
        IOptions<PaymentWorkerOptions> optionsAccessor)
    {
        this.externalReader = externalReader;
        this.redis = redis;
        this.options = optionsAccessor.Value;

        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(this.options.PooledConnectionLifetimeMinutes),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(this.options.PooledConnectionIdleTimeoutMinutes),
            MaxConnectionsPerServer = this.options.ConnectionsPerServer,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(this.options.ConnectTimeoutSeconds),
            ResponseDrainTimeout = TimeSpan.FromSeconds(1)
        };

        this.httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(this.options.HttpTimeoutSeconds),
            DefaultRequestHeaders = {
                { "Accept", "application/json" },
                { "User-Agent", "PaymentProcessor/1.0" }
            }
        };

        this.defaultProcessor = new Uri("http://payment-processor-default:8080/payments");

        this.retryQueue = Channel.CreateUnbounded<RetryPayment>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });

        this.maxConcurrency = Math.Max(Environment.ProcessorCount * 4, 20);
        this.concurrencyLimiter = new SemaphoreSlim(this.options.MaxConcurrency, this.options.MaxConcurrency);
        this.taskFactory = new TaskFactory(TaskScheduler.Default);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task[] mainProcessors = Enumerable.Range(0, Environment.ProcessorCount)
            .Select(_ => this.ProcessMainQueueAsync(stoppingToken))
            .ToArray();

        Task[] retryProcessors = Enumerable.Range(0, 2)
            .Select(_ => this.ProcessRetryQueueAsync(stoppingToken))
            .ToArray();

        await Task.WhenAll(mainProcessors.Concat(retryProcessors));
    }

    private async Task ProcessMainQueueAsync(CancellationToken ct)
    {
        await foreach (Payment payment in this.externalReader.ReadAllAsync(ct))
        {
            await this.concurrencyLimiter.WaitAsync(ct);

            _ = this.taskFactory.StartNew(async () =>
            {
                try
                {
                    bool success = await this.ProcessPaymentAsync(payment, ct);

                    if (success)
                    {
                        Interlocked.Increment(ref this.processedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref this.failedCount);

                        RetryPayment retryPayment = new(payment, 0, DateTime.UtcNow.AddMilliseconds(500));
                        await this.retryQueue.Writer.WriteAsync(retryPayment, ct);
                    }
                }
                finally
                {
                    this.concurrencyLimiter.Release();
                }
            }, ct, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ProcessRetryQueueAsync(CancellationToken ct)
    {
        await foreach (RetryPayment retryPayment in this.retryQueue.Reader.ReadAllAsync(ct))
        {
            DateTime now = DateTime.UtcNow;
            if (retryPayment.RetryAt > now)
            {
                TimeSpan delay = retryPayment.RetryAt - now;
                await Task.Delay(delay, ct);
            }

            await this.concurrencyLimiter.WaitAsync(ct);

            _ = this.taskFactory.StartNew(async () =>
            {
                try
                {
                    bool success = await this.ProcessPaymentAsync(retryPayment.Payment, ct);

                    if (success)
                    {
                        Interlocked.Increment(ref this.processedCount);
                    }
                    else if (retryPayment.Attempt < this.options.MaxRetryAttempts)
                    {
                        Interlocked.Increment(ref this.retryCount);

                        int nextAttempt = retryPayment.Attempt + 1;
                        double baseDelay = Math.Min(1000 * Math.Pow(2, nextAttempt), 30000);
                        int jitter = Random.Shared.Next(0, (int)(baseDelay * 0.1));
                        DateTime retryAt = DateTime.UtcNow.AddMilliseconds(baseDelay + jitter);

                        RetryPayment newRetryPayment = retryPayment with
                        {
                            Attempt = nextAttempt,
                            RetryAt = retryAt
                        };

                        await this.retryQueue.Writer.WriteAsync(newRetryPayment, ct);
                    }
                }
                finally
                {
                    this.concurrencyLimiter.Release();
                }
            }, ct, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        string requestedAt = now.ToString("o");

        PaymentRecord paymentRecord = new(payment.CorrelationId, payment.Amount, requestedAt);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(this.options.HttpTimeoutSeconds));

        HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(
            this.defaultProcessor,
            paymentRecord,
            JsonContext.Default.PaymentRecord,
            cts.Token);

        if (response.IsSuccessStatusCode)
        {
            _ = this.WriteToRedisAsync(payment, now, ct);
            return true;
        }

        return false;
    }
    private async Task WriteToRedisAsync(Payment payment, DateTime timestamp, CancellationToken ct)
    {
        string key = "payments:default";
        string member = $"{payment.CorrelationId}:{payment.Amount}";
        double score = timestamp.Subtract(DateTime.UnixEpoch).TotalMilliseconds;

        await this.redis.SortedSetAddAsync(key, member, score);
    }

    public new void Dispose()
    {
        this.httpClient?.Dispose();
        this.concurrencyLimiter?.Dispose();
        this.retryQueue?.Writer?.Complete();
        base.Dispose();
    }
}

public record RetryPayment(Payment Payment, int Attempt = 0, DateTime RetryAt = default);