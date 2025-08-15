using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Infra;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using System.Threading.Channels;

namespace PaymentProcessor.Api.Services;

public sealed class PaymentProcessorWorker : BackgroundService
{
    private readonly ChannelReader<Payment> reader;
    private readonly IDatabase redis;
    private readonly HttpClient httpClient;

    private readonly Uri defaultProcessor;
    private readonly Uri fallbackProcessor;

    private readonly AsyncRetryPolicy<HttpResponseMessage> retryPolicy;
    private readonly DateTime lastHealthCheckDefault = DateTime.MinValue;
    private readonly DateTime lastHealthCheckFallback = DateTime.MinValue;
    private readonly bool defaultHealthy = true;
    private readonly bool fallbackHealthy = true;

    public PaymentProcessorWorker(ChannelReader<Payment> reader, RedisConnection redisConn)
    {
        this.reader = reader;
        this.redis = redisConn.Database;
        this.httpClient = new HttpClient();

        this.defaultProcessor = new Uri("http://payment-processor-default:8080");
        this.fallbackProcessor = new Uri("http://payment-processor-fallback:8080");

        Random jitterer = new();
        this.retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(msg => !msg.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(100 * Math.Pow(2, attempt)) +
                    TimeSpan.FromMilliseconds(jitterer.Next(0, 50))
            );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until there's at least one item or channel completes
            if (await this.reader.WaitToReadAsync(stoppingToken))
            {
                await Parallel.ForEachAsync(
                    this.reader.ReadAllAsync(stoppingToken),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 5,
                        CancellationToken = stoppingToken
                    },
                    async (payment, token) =>
                    {
                        await this.ProcessAndCommitAsync(payment, token);
                    });
            }
            else
            {
                // Channel is completed — break the loop
                break;
            }

            // Optional: small delay to avoid tight looping when idle
            await Task.Delay(50, stoppingToken);
        }
    }

    private async Task ProcessAndCommitAsync(Payment payment, CancellationToken token)
    {
        DateTime now = DateTime.UtcNow;
        string requestedAt = now.ToString("o");

        bool success = await this.ProcessPaymentAsync(payment, requestedAt, token);
        if (success)
        {
            string key = "payments:default";
            string member = $"{payment.CorrelationId}:{payment.Amount}";

            await this.redis.SortedSetAddAsync(
                key,
                member,
                now.Subtract(DateTime.UnixEpoch).TotalMilliseconds
            ).ConfigureAwait(false);
        }
    }

    private async Task<bool> ProcessPaymentAsync(Payment payment, string requestedAt, CancellationToken token)
    {
        Uri target = this.ChooseProcessorAsync(token);

        var payload = new
        {
            correlationId = payment.CorrelationId,
            amount = payment.Amount,
            requestedAt
        };

        HttpResponseMessage response = await this.retryPolicy.ExecuteAsync(() =>
            this.httpClient.PostAsJsonAsync(new Uri(target, "payments"), payload, token));

        return response.IsSuccessStatusCode;
    }

    private Uri ChooseProcessorAsync(CancellationToken token)
    {
        // aqui entra a lógica de health-check / fallback que discutimos
        // Exemplo simplificado: sempre tenta default primeiro
        return this.defaultProcessor;
    }
}