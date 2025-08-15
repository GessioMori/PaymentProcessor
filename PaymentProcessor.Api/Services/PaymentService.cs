using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Infra;
using StackExchange.Redis;
using System.Globalization;
using System.Threading.Channels;

namespace PaymentProcessor.Api.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly ChannelWriter<Payment> writer;
    private readonly IDatabase redis;

    private const string defaultKey = "payments:default";
    private const string fallbackKey = "payments:fallback";

    public PaymentService(ChannelWriter<Payment> writer, RedisConnection redisConn)
    {
        this.writer = writer;
        this.redis = redisConn.Database;
    }

    public async Task<StatsResponse> GetSummaryAsync(DateTime from, DateTime to)
    {
        double fromScore = from.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds;

        double toScore = to.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds;

        // Default
        RedisValue[] defaultValues = await this.redis.SortedSetRangeByScoreAsync(defaultKey, fromScore, toScore).ConfigureAwait(false);
        int defaultTotalRequests = defaultValues.Length;
        decimal defaultTotalAmount = defaultValues.Sum(x => decimal.Parse(x.ToString().Split(':')[1], CultureInfo.InvariantCulture));

        // Fallback
        RedisValue[] fallbackValues = await this.redis.SortedSetRangeByScoreAsync(fallbackKey, fromScore, toScore).ConfigureAwait(false);
        int fallbackTotalRequests = fallbackValues.Length;
        decimal fallbackTotalAmount = fallbackValues.Sum(x => decimal.Parse(x.ToString().Split(':')[1], CultureInfo.InvariantCulture));

        return new StatsResponse(new RequestStats(defaultTotalRequests, defaultTotalAmount),
            new RequestStats(fallbackTotalRequests, fallbackTotalAmount));
    }

    public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await this.writer.WriteAsync(payment, cancellationToken).ConfigureAwait(false);
    }
}