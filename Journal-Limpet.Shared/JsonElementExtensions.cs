using Journal_Limpet.Shared.Models.User;
using System.Text.Json;

namespace Journal_Limpet.Shared
{
    public static class JsonElementExtensions
    {
        public static T GetTypedObject<T>(this JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }

        public static JsonElement AsJsonElement(this IIntegrationBaseSettings integrationSetting)
        {
            var json = JsonSerializer.Serialize((object)integrationSetting);

            return JsonDocument.Parse(json).RootElement;
        }
    }
}
