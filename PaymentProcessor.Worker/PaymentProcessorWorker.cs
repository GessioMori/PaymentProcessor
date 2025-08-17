using Microsoft.Extensions.Options;
using PaymentProcessor.Shared;
using StackExchange.Redis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace PaymentProcessor.Worker;

public sealed class PaymentProcessorWorker : BackgroundService, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly Uri defaultProcessor;
    private readonly Channel<Payment> retryQueue;
    private readonly IDatabase redis;

    private readonly PaymentWorkerOptions options;

    private readonly SemaphoreSlim concurrency = new(8);

    public PaymentProcessorWorker(IDatabase redis, IOptions<PaymentWorkerOptions> optionsAccessor)
    {
        this.redis = redis;
        this.options = optionsAccessor.Value;

        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(this.options.PooledConnectionLifetimeMinutes),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(this.options.PooledConnectionIdleTimeoutMinutes),
            MaxConnectionsPerServer = 8,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
            ResponseDrainTimeout = TimeSpan.FromSeconds(1)
        };

        this.httpClient = new HttpClient(handler)
        {
            DefaultRequestHeaders = {
                { "Accept", "application/json" },
                { "User-Agent", "PaymentProcessor/1.0" }
            }
        };

        this.defaultProcessor = new Uri("http://payment-processor-default:8080/payments");

        this.retryQueue = Channel.CreateUnbounded<Payment>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task[] mainProcessors = Enumerable.Range(0, 4)
            .Select(_ => this.ProcessMainQueueAsync(stoppingToken))
            .ToArray();

        Task[] retryProcessors = Enumerable.Range(0, 4)
            .Select(_ => this.ProcessRetryQueueAsync(stoppingToken))
            .ToArray();

        await Task.WhenAll(mainProcessors.Concat(retryProcessors));
    }

    private async Task ProcessMainQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            RedisValue value = await this.redis.ListRightPopAsync("payments:queue");

            if (!value.HasValue)
            {
                await Task.Delay(20, ct);
                continue;
            }

            Payment? payment = JsonSerializer.Deserialize(value!, JsonContext.Default.Payment);

            if (payment is null) continue;

            bool success = await this.ProcessPaymentAsync(payment, ct);

            if (!success)
            {
                await this.retryQueue.Writer.WriteAsync(payment, ct);
            }
        }
    }

    private async Task ProcessRetryQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (this.retryQueue.Reader.TryRead(out Payment? payment))
            {
                if (payment is null)
                {
                    await Task.Delay(5, ct);
                    continue;
                }

                bool success = await this.ProcessPaymentAsync(payment, ct);
                if (!success)
                {
                    await this.retryQueue.Writer.WriteAsync(payment, ct);
                }
            }
            else
            {
                await Task.Delay(5, ct);
            }
        }
    }
    private async Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken ct)
    {
        await this.concurrency.WaitAsync(ct);

        DateTime now = DateTime.UtcNow;

        try
        {
            string requestedAt = now.ToString("o");
            PaymentRecord paymentRecord = new(payment.CorrelationId, payment.Amount, requestedAt);

            HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(
                this.defaultProcessor, paymentRecord, JsonContext.Default.PaymentRecord, ct);

            if (response.IsSuccessStatusCode)
            {
                this.WriteToRedisAsync(payment, now, ct);
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            this.concurrency.Release();
        }
    }

    private void WriteToRedisAsync(Payment payment, DateTime timestamp, CancellationToken ct)
    {
        string key = "payments:default";
        string member = $"{payment.CorrelationId}:{payment.Amount}";
        double score = timestamp.Subtract(DateTime.UnixEpoch).TotalMilliseconds;

        _ = this.redis.SortedSetAddAsync(key, member, score);
    }

    public new void Dispose()
    {
        this.httpClient?.Dispose();
        this.retryQueue?.Writer?.Complete();
        base.Dispose();
    }
}
