using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Serialization;
using PaymentProcessor.Api.Services;
using StackExchange.Redis;
using System.Text.Json;
using System.Threading.Channels;

public class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = JsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(options =>
        {
            string socketPath = builder.Configuration["SOCKET_PATH"]!;
            options.ListenUnixSocket(socketPath);

            options.Limits.MaxConcurrentConnections = 1000;
            options.Limits.MaxConcurrentUpgradedConnections = 1000;
            options.Limits.MaxRequestBodySize = 1024 * 1024; // 1MB
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });

        Channel<Payment> channel = Channel.CreateBounded<Payment>(new BoundedChannelOptions(100_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = true
        });

        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<ChannelWriter<Payment>>(channel.Writer);
        builder.Services.AddSingleton<ChannelReader<Payment>>(channel.Reader);

        builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            ConfigurationOptions configuration = ConfigurationOptions.Parse("redis:6379,abortConnect=false");
            configuration.ConnectTimeout = 5000;
            configuration.SyncTimeout = 5000;
            configuration.AsyncTimeout = 5000;
            configuration.ConnectRetry = 3;
            configuration.AbortOnConnectFail = false;
            configuration.KeepAlive = 60;
            configuration.DefaultDatabase = 0;
            configuration.Ssl = false;
            configuration.AllowAdmin = false;
            return ConnectionMultiplexer.Connect(configuration);
        });

        builder.Services.Configure<PaymentWorkerOptions>(options =>
        {
            options.MaxConcurrency = 5;
            options.HttpTimeoutSeconds = 3;
            options.MaxRetryAttempts = 5;
            options.ConnectionsPerServer = 50;
            options.PooledConnectionLifetimeMinutes = 10;
            options.PooledConnectionIdleTimeoutMinutes = 2;
            options.ConnectTimeoutSeconds = 2;
        });

        builder.Services.AddSingleton<IDatabase>(provider =>
            provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.AddHostedService<PaymentProcessorWorker>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultBufferSize = 16 * 1024; // Buffer maior
        });

        WebApplication app = builder.Build();

        app.MapPost("/payments", (Payment payment, ChannelWriter<Payment> writer) =>
        {
            bool written = writer.TryWrite(payment);
            return written ? Results.Ok() : Results.StatusCode(503);
        })
        .WithName("ProcessPayment");

        app.MapGet("/payments-summary", async (DateTime from, DateTime to, IDatabase redis) =>
        {
            double fromScore = from.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds;
            double toScore = to.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds;

            const string luaScript = @"
                local values = redis.call('ZRANGEBYSCORE', KEYS[1], ARGV[1], ARGV[2])
                local count = #values
                local total = 0
                
                for i = 1, count do
                    local parts = {}
                    for part in string.gmatch(values[i], '([^:]+)') do
                        table.insert(parts, part)
                    end
                    if #parts >= 2 then
                        total = total + tonumber(parts[2])
                    end
                end
                
                return {count, total}
            ";

            // Executar Lua script no Redis
            RedisResult result = await redis.ScriptEvaluateAsync(luaScript,
                ["payments:default"],
                [fromScore, toScore]);

            RedisResult[] resultArray = (RedisResult[])result!;
            int totalRequests = (int)resultArray[0];
            decimal totalAmount = (decimal)(double)resultArray[1];

            StatsResponse summary = new(
                new RequestStats(totalRequests, totalAmount),
                new RequestStats(0, 0)
            );

            return Results.Json(summary, JsonOptions);
        })
        .WithName("ProcessPaymentSummary");

        app.Run();
    }
}