
using PaymentProcessor.Api.Entities;
using PaymentProcessor.Api.Infra;
using PaymentProcessor.Api.Serialization;
using PaymentProcessor.Api.Services;
using System.Text.Json;
using System.Threading.Channels;

namespace PaymentProcessor.Api;

public class Program
{
    public static void Main(string[] args)
    {
        ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(options =>
        {
            string urls = builder.Configuration["SOCKET_PATH"]!;
            options.ListenUnixSocket(urls);
        });

        Channel<Payment> channel = Channel.CreateBounded<Payment>(new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<ChannelWriter<Payment>>(channel.Writer);
        builder.Services.AddSingleton<ChannelReader<Payment>>(channel.Reader);

        builder.Services.AddSingleton<RedisConnection>();
        builder.Services.AddSingleton<IPaymentService, PaymentService>();
        builder.Services.AddHostedService<PaymentProcessorWorker>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        WebApplication app = builder.Build();

        app.UseHttpsRedirection();

        app.MapPost("/payments", (Payment payment, IPaymentService paymentService) =>
        {
            paymentService.ProcessPaymentAsync(payment);
            return Results.Ok();
        })
            .WithName("ProcessPayment");

        app.MapGet("/payments-summary", async (DateTime from, DateTime to, IPaymentService paymentService) =>
                {
                    StatsResponse summary = await paymentService.GetSummaryAsync(from, to);

                    return Results.Json(summary, new JsonSerializerOptions
                    {
                        TypeInfoResolver = JsonContext.Default,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                })
            .WithName("ProcessPaymentSummary");

        app.Run();
    }
}