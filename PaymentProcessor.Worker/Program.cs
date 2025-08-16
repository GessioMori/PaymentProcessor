using StackExchange.Redis;

namespace PaymentProcessor.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

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

        builder.Services.AddSingleton<IDatabase>(provider =>
            provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.Configure<PaymentWorkerOptions>(options =>
        {
            options.MaxConcurrency = 5;
            options.HttpTimeoutSeconds = 3;
            options.MaxRetryAttempts = 10;
            options.ConnectionsPerServer = 1000;
            options.PooledConnectionLifetimeMinutes = 10;
            options.PooledConnectionIdleTimeoutMinutes = 2;
            options.ConnectTimeoutSeconds = 2;
        });

        builder.Services.AddHostedService<PaymentProcessorWorker>();

        IHost host = builder.Build();
        host.Run();
    }
}