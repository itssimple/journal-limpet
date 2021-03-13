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
        public static async Task<bool> GetSystemCache(Dictionary<string, JsonElement> elementAsDictionary, IDatabase _rdb, StarSystemChecker starSystemChecker)
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

                var arrayEnum = elementAsDictionary["StarPos"].EnumerateArray().ToArray();

                var edSysData = new EDSystemData
                {
                    Id64 = elementAsDictionary["SystemAddress"].GetInt64(),
                    Name = elementAsDictionary["StarSystem"].GetString(),
                    Coordinates = new EDSystemCoordinates
                    {
                        X = arrayEnum[0].GetDouble(),
                        Y = arrayEnum[1].GetDouble(),
                        Z = arrayEnum[2].GetDouble()
                    }
                };

                await starSystemChecker.InsertOrUpdateSystemAsync(edSysData);

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
                else
                {
                    var systemData = await starSystemChecker.GetSystemDataAsync(elementAsDictionary["SystemAddress"].GetInt64());
                    if (systemData != null)
                    {
                        var jel = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            SystemAddress = systemData.Id64,
                            StarSystem = systemData.Name,
                            StarPos = new[] { systemData.Coordinates.X, systemData.Coordinates.Y, systemData.Coordinates.Z }
                        })).RootElement;

                        elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                        elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                        elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                    }
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
            }
        }
    }
}
