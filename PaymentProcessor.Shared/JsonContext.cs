using System.Text.Json.Serialization;

namespace PaymentProcessor.Shared;

[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(PaymentRecord))]
[JsonSerializable(typeof(StatsResponse))]
public partial class JsonContext : JsonSerializerContext
{
}
