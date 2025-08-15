using StackExchange.Redis;

namespace PaymentProcessor.Api.Infra;

public sealed class RedisConnection
{
    private readonly ConnectionMultiplexer _connection;
    public IDatabase Database => this._connection.GetDatabase();

    public RedisConnection(IConfiguration config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config), "Configuration cannot be null.");
        }

        string? connectionString = config["Redis:ConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Redis connection string is not configured.", nameof(config));
        }

        this._connection = ConnectionMultiplexer.Connect(connectionString);

        IDatabase db = this.Database;
        db.KeyDelete("payments:default");
        db.KeyDelete("payments:fallback");
    }
}