namespace TaskerCore.Models;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Priority
{
    High = 1,
    Medium = 2,
    Low = 3
}
