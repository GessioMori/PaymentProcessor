
using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Infra;
using PaymentProcessor.Api.Services;
using System.Threading.Channels;

namespace PaymentProcessor.Api;

public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        Channel<Payment> channel = Channel.CreateUnbounded<Payment>();
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<ChannelWriter<Payment>>(channel.Writer);
        builder.Services.AddSingleton<ChannelReader<Payment>>(channel.Reader);

        builder.Services.AddSingleton<RedisConnection>();
        builder.Services.AddSingleton<IPaymentService, PaymentService>();
        builder.Services.AddHostedService<PaymentProcessorWorker>();

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}