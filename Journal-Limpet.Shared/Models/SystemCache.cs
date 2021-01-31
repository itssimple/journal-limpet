using Journal_Limpet.Shared.Models.Journal;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared.Models
{
    public enum RequiredPropertiesForCache
    {
        StarSystem,
        StarPos,
        SystemAddress,
        timestamp,
        @event
    }

    public static class EDSystemCache
    {
        public static async Task<bool> GetSystemCache(Dictionary<string, JsonElement> elementAsDictionary, IDatabase _rdb)
        {
            var reqProps = typeof(RequiredPropertiesForCache).GetEnumNames();

            var requiredProperties = elementAsDictionary.Keys.Where(k => Enum.TryParse(typeof(RequiredPropertiesForCache), k, false, out _));
            var missingProps = reqProps.Except(requiredProperties);

            bool setCache = false;

            if (!missingProps.Any())
            {
                await _rdb.StringSetAsyncWithRetries(
                    $"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                await _rdb.StringSetAsyncWithRetries(
                    $"StarSystem:{elementAsDictionary["StarSystem"].GetString()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                setCache = true;
            }

            var importantProps = new[] { "StarPos", "StarSystem", "SystemAddress" };

            if (!missingProps.Contains("SystemAddress"))
            {
                var cachedSystem = await _rdb.StringGetAsyncWithRetries($"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");

                    setCache = true;
                }
            }
            else if (!missingProps.Contains("StarSystem"))
            {
                var cachedSystem = await _rdb.StringGetAsyncWithRetries($"StarSystem:{elementAsDictionary["StarSystem"].GetString()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");

                    setCache = true;
                }
            }

            return setCache;
        }

        public static async Task SetSystemCache(EDGameState gameState, IDatabase _rdb, bool setCache)
        {
            if (!setCache && gameState.SystemAddress.HasValue && gameState.SystemCoordinates.HasValue && !string.IsNullOrWhiteSpace(gameState.SystemName))
            {
                await _rdb.StringSetAsyncWithRetries(
                    $"SystemAddress:{gameState.SystemAddress}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = gameState.SystemAddress,
                        StarSystem = gameState.SystemName,
                        StarPos = gameState.SystemCoordinates
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                await _rdb.StringSetAsyncWithRetries(
                    $"StarSystem:{gameState.SystemName}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = gameState.SystemAddress,
                        StarSystem = gameState.SystemName,
                        StarPos = gameState.SystemCoordinates
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );
            }
        }
    }
}
