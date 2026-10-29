using System.Text.Json;

namespace GearFlow.Os.Application.Messaging;

public static class OsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
