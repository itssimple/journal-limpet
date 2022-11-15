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
                /*await _rdb.StringSetAsyncWithRetries(
                    $"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );*/

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

                // Since the event has all properties, we allow setting the cache
                //setCache = true;
            }

            var importantProps = new[] { "StarPos", "StarSystem", "SystemAddress" };

            if (!missingProps.Contains("SystemAddress"))
            {
                /*var cachedSystem = await _rdb.StringGetAsyncWithRetries($"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    // Don't replace values that already exists on the event, supposedly the journal is supposed to be correct on those already
                    //elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    if (!elementAsDictionary.ContainsKey("StarSystem"))
                    {
                        elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    }
                    if (!elementAsDictionary.ContainsKey("StarPos"))
                    {
                        elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                    }

                    // Do not allow setting the cache, just because we fetched it from the cache.
                    setCache = false;
                }
                else*/
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

                        // Don't replace values that already exists on the event, supposedly the journal is supposed to be correct on those already
                        //elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                        if (!elementAsDictionary.ContainsKey("StarSystem"))
                        {
                            elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                        }
                        if (!elementAsDictionary.ContainsKey("StarPos"))
                        {
                            elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                        }

                        // It's safe to set the cache here, since we fetch the data from the database
                        //setCache = true;
                    }
                }
            }

            return setCache;
        }

        public static async Task SetSystemCache(EDGameState gameState, IDatabase _rdb, bool setCache)
        {
            /*if (!setCache && gameState.SystemAddress.HasValue && gameState.SystemCoordinates.HasValue && !string.IsNullOrWhiteSpace(gameState.SystemName))
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
            }*/

            await Task.CompletedTask;
        }
    }
}
