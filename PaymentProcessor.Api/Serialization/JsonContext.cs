using PaymentProcessor.Api.Entities;
using System.Text.Json.Serialization;

namespace PaymentProcessor.Api.Serialization;

[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(PaymentRecord))]
[JsonSerializable(typeof(StatsResponse))]
public partial class JsonContext : JsonSerializerContext
{
}
