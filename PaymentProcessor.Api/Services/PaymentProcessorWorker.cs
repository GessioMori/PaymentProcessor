using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Infra;
using PaymentProcessor.Api.Serialization;
using StackExchange.Redis;
using System.Threading.Channels;

namespace PaymentProcessor.Api.Services;

public sealed class PaymentProcessorWorker : BackgroundService
{
    private readonly ChannelReader<Payment> externalReader;
    private readonly HttpClient httpClient;
    private readonly Uri defaultProcessor;
    private readonly Uri fallbackProcessor;

    private readonly Channel<RetryPayment> retryQueue;
    private readonly SemaphoreSlim mainParallelism;
    private readonly SemaphoreSlim retryParallelism;

    private readonly IDatabase redis;

    public PaymentProcessorWorker(ChannelReader<Payment> externalReader, RedisConnection redisConn)
    {
        this.externalReader = externalReader;
        this.httpClient = new HttpClient();
        this.defaultProcessor = new Uri("http://payment-processor-default:8080/payments");
        this.fallbackProcessor = new Uri("http://payment-processor-fallback:8080/payments");

        this.retryQueue = Channel.CreateUnbounded<RetryPayment>();

        this.mainParallelism = new SemaphoreSlim(5);
        this.retryParallelism = new SemaphoreSlim(3);

        this.redis = redisConn.Database;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => this.ProcessMainQueueAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => this.ProcessRetryQueueAsync(stoppingToken), stoppingToken);

        await Task.CompletedTask;
    }

    private async Task ProcessMainQueueAsync(CancellationToken ct)
    {
        await foreach (Payment payment in this.externalReader.ReadAllAsync(ct))
        {
            await this.mainParallelism.WaitAsync(ct);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await this.ProcessPaymentAsync(payment, ct))
                    {
                        await this.retryQueue.Writer.WriteAsync(new RetryPayment(payment), ct);
                    }
                }
                finally
                {
                    this.mainParallelism.Release();
                }
            }, ct);
        }
    }

    private async Task ProcessRetryQueueAsync(CancellationToken ct)
    {
        await foreach (RetryPayment retryPayment in this.retryQueue.Reader.ReadAllAsync(ct))
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryPayment.Attempt + 1) + Random.Shared.Next(0, 50));

            await Task.Delay(delay, ct);

            await this.retryParallelism.WaitAsync(ct);

            _ = Task.Run(async () =>
            {
                try
                {
                    bool success = await this.ProcessPaymentAsync(retryPayment.Payment, ct);
                    if (!success && retryPayment.Attempt < 10)
                    {
                        await this.retryQueue.Writer.WriteAsync(retryPayment with { Attempt = retryPayment.Attempt + 1 }, ct);
                    }
                }
                finally
                {
                    this.retryParallelism.Release();
                }
            }, ct);
        }
    }

    private async Task<bool> ProcessPaymentAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            string requestedAt = now.ToString("o");

            PaymentRecord paymentRecord = new(payment.CorrelationId, payment.Amount, requestedAt);

            HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(this.ChooseProcessor(), paymentRecord, JsonContext.Default.PaymentRecord, ct);

            if (response.IsSuccessStatusCode)
            {
                string key = "payments:default";
                string member = $"{payment.CorrelationId}:{payment.Amount}";
                await this.redis.SortedSetAddAsync(
                    key,
                    member,
                    now.Subtract(DateTime.UnixEpoch).TotalMilliseconds
                ).ConfigureAwait(false);
            }

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private Uri ChooseProcessor() => this.defaultProcessor;
}