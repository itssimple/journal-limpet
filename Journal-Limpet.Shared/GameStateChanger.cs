using Journal_Limpet.Shared.Models.Journal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Journal_Limpet.Shared
{
    public static class GameStateChanger
    {
        public static void GameStateFixer(EDGameState gameState, string commander, Dictionary<string, JsonElement> elementAsDictionary)
        {
            var eventName = elementAsDictionary["event"].GetString();
            var timestamp = elementAsDictionary["timestamp"].GetDateTimeOffset();

            gameState.Timestamp = timestamp;

            if (OdysseyEvents.Contains(eventName))
            {
                gameState.Odyssey = true;
            }

            if (elementAsDictionary.ContainsKey("SystemAddress"))
            {
                if (!IgnoredChangedSystemAddressEvents.Contains(eventName))
                {
                    if (elementAsDictionary["SystemAddress"].GetInt64() != gameState.SystemAddress)
                    {
                        gameState.SystemAddress = null;
                        gameState.SystemName = null;
                        gameState.SystemCoordinates = null;
                        gameState.MarketId = null;
                        gameState.StationName = null;
                        gameState.BodyId = null;
                        gameState.BodyName = null;
                    }
                }
            }

            // We'll disable this reset for now, since commanders do re-log at times
            // And for some reason, Location wasn't written to the journals,
            // maybe it only appears if you actually restart the entire game?

            if (eventName == "LoadGame")
            {
                //gameState.SystemAddress = null;
                //gameState.SystemName = null;
                //gameState.SystemCoordinates = null;
                //gameState.MarketId = null;
                //gameState.StationName = null;
                //gameState.ShipId = null;
                //gameState.BodyId = null;
                //gameState.BodyName = null;
                if (elementAsDictionary.ContainsKey("Odyssey"))
                {
                    gameState.Odyssey = elementAsDictionary["Odyssey"].GetBoolean();
                }
            }

            if (eventName == "Rank")
            {
                if (elementAsDictionary.ContainsKey("Soldier") || elementAsDictionary.ContainsKey("Exobiologist"))
                {
                    gameState.Odyssey = true;
                }
            }

            if (eventName == "SetUserShipName")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "ShipyardBuy")
            {
                gameState.ShipId = null;
            }

            if (eventName == "ShipyardSwap")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "Loadout")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "NavBeaconScan")
            {
                SetSystemAddress(gameState, elementAsDictionary, false);
            }

            if (eventName == "Undocked")
            {
                gameState.MarketId = null;
                gameState.StationName = null;
                gameState.BodyId = null;
                gameState.BodyName = null;
            }

            if (eventName == "ApproachBody")
            {
                SetStarSystem(gameState, elementAsDictionary, false);
                SetSystemAddress(gameState, elementAsDictionary, false);
                SetBodyName(gameState, elementAsDictionary, false);
                SetBodyID(gameState, elementAsDictionary, false);
            }

            if (eventName == "LeaveBody")
            {
                gameState.BodyId = null;
                gameState.BodyName = null;
            }

            if (eventName == "SupercruiseEntry")
            {
                if (elementAsDictionary.ContainsKey("StarSystem"))
                {
                    if (elementAsDictionary["StarSystem"].GetString() != gameState.SystemName)
                    {
                        gameState.SystemCoordinates = null;
                        gameState.SystemAddress = null;
                        gameState.BodyId = null;
                        gameState.BodyName = null;
                    }

                    SetStarSystem(gameState, elementAsDictionary, false);
                }

                SetSystemAddress(gameState, elementAsDictionary, false);

                gameState.BodyName = null;
                gameState.BodyId = null;
            }

            if (eventName == "ShipLockerMaterials")
            {
                gameState.Odyssey = true;
            }

            if (eventName == "SupercruiseExit")
            {
                SetStarSystem(gameState, elementAsDictionary, false);

                SetBodyName(gameState, elementAsDictionary, true);
                SetBodyID(gameState, elementAsDictionary, true);
            }

            if (new[] { "Location", "FSDJump", "Docked", "CarrierJump" }.Contains(eventName))
            {
                // Docked don"t have coordinates, if system changed reset
                if (elementAsDictionary["StarSystem"].GetString() != gameState.SystemName)
                {
                    gameState.SystemCoordinates = null;
                    gameState.SystemAddress = null;
                    gameState.BodyId = null;
                    gameState.BodyName = null;
                }

                if (elementAsDictionary.ContainsKey("StationServices"))
                {
                    if (elementAsDictionary["StationServices"].GetRawText().Contains("socialspace"))
                    {
                        gameState.Odyssey = true;
                    }
                }

                if (elementAsDictionary["StarSystem"].GetString() != "ProvingGround" && elementAsDictionary["StarSystem"].GetString() != "CQC")
                {
                    SetSystemAddress(gameState, elementAsDictionary, false);
                    SetStarSystem(gameState, elementAsDictionary, false);
                    SetStarPos(gameState, elementAsDictionary, false);

                    SetBodyName(gameState, elementAsDictionary, false);
                    SetBodyID(gameState, elementAsDictionary, false);
                }
                else
                {
                    gameState.SystemAddress = null;
                    gameState.SystemName = null;
                    gameState.SystemCoordinates = null;
                    gameState.BodyId = null;
                    gameState.BodyName = null;
                }

                if (elementAsDictionary.ContainsKey("MarketID"))
                {
                    gameState.MarketId = elementAsDictionary["MarketID"].GetInt64();
                }
                if (elementAsDictionary.ContainsKey("StationName"))
                {
                    gameState.StationName = elementAsDictionary["StationName"].GetString();
                }
            }

            if (new[] { "SAASignalsFound", "SAAScanComplete", "Scan", }.Contains(eventName))
            {
                SetSystemAddress(gameState, elementAsDictionary, false);
                SetStarSystem(gameState, elementAsDictionary, false);
                SetStarPos(gameState, elementAsDictionary, false);
            }

            if (new[] { "FSSDiscoveryScan", "CodexEntry", "FSSAllBodiesFound", "Touchdown", "Liftoff", "ApproachSettlement", "Disembark", "Embark" }.Contains(eventName))
            {
                SetSystemAddress(gameState, elementAsDictionary, false);
                SetStarSystem(gameState, elementAsDictionary, false);
                SetStarPos(gameState, elementAsDictionary, false);

                SetBodyName(gameState, elementAsDictionary, false);
                SetBodyID(gameState, elementAsDictionary, false);
            }

            if (new[] { "JoinACrew", "QuitACrew" }.Contains(eventName))
            {
                if (eventName == "JoinACrew" && elementAsDictionary.ContainsKey("Captain") && elementAsDictionary["Captain"].GetString() != commander)
                {
                    gameState.SendEvents = false;
                }

                else
                {
                    gameState.SendEvents = true;
                }

                gameState.SystemAddress = null;
                gameState.SystemName = null;
                gameState.SystemCoordinates = null;
                gameState.MarketId = null;
                gameState.StationName = null;
                gameState.ShipId = null;
                gameState.BodyId = null;
                gameState.BodyName = null;
            }

            if (elementAsDictionary.ContainsKey("Taxi"))
            {
                gameState.Odyssey = true;
            }

            if (elementAsDictionary.ContainsKey("OnFoot"))
            {
                gameState.Odyssey = true;
            }
        }

        private static void SetBodyID(EDGameState gameState, Dictionary<string, JsonElement> elementAsDictionary, bool nullIfMissing)
        {
            if (elementAsDictionary.ContainsKey("BodyID"))
            {
                gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
            }
            else
            {
                if (nullIfMissing)
                {
                    gameState.BodyId = null;
                }
            }
        }

        private static void SetBodyName(EDGameState gameState, Dictionary<string, JsonElement> elementAsDictionary, bool nullIfMissing)
        {
            if (elementAsDictionary.ContainsKey("BodyName"))
            {
                gameState.BodyName = elementAsDictionary["BodyName"].GetString();
            }
            else
            {
                if (nullIfMissing)
                {
                    gameState.BodyName = null;
                }
            }
        }

        private static void SetStarPos(EDGameState gameState, Dictionary<string, JsonElement> elementAsDictionary, bool nullIfMissing)
        {
            if (elementAsDictionary.ContainsKey("StarPos"))
            {
                gameState.SystemCoordinates = elementAsDictionary["StarPos"];
            }
            else
            {
                if (nullIfMissing)
                {
                    gameState.SystemCoordinates = null;
                }
            }
        }

        private static void SetStarSystem(EDGameState gameState, Dictionary<string, JsonElement> elementAsDictionary, bool nullIfMissing)
        {
            if (elementAsDictionary.ContainsKey("StarSystem"))
            {
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();
            }
            else
            {
                if (nullIfMissing)
                {
                    gameState.SystemName = null;
                }
            }
        }

        private static void SetSystemAddress(EDGameState gameState, Dictionary<string, JsonElement> elementAsDictionary, bool nullIfMissing)
        {
            if (elementAsDictionary.ContainsKey("SystemAddress"))
            {
                gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
            }
            else
            {
                if (nullIfMissing)
                {
                    gameState.SystemAddress = null;
                }
            }
        }

        public readonly static string[] OdysseyEvents = new string[] {
            "ShipLockerMaterials",
            "SuitLoadout",
            "BackPack",
            "BookTaxi",
            "BuyMicroResources",
            "TransferMicroResources",
            "CancelTaxi",
            "BuySuit",
            "BuyWeapon",
            "CreateSuitLoadout",
            "SwitchSuitLoadout",
            "ShieldState",
            "BackpackChange",
            "LoadoutEquipModule",
            "CancelDropship",
            "CollectItems",
            "UseConsumable",
            "BackPackMaterials",
            "ScanOrganic",
            "SellOrganicData",
            "DeleteSuitLoadout",
            "DropshipDeploy",
            "BookDropship",
            "SellSuit",
            "TradeMicroResources",
            "SellMicroResources"
        };

        public readonly static string[] IgnoredChangedSystemAddressEvents = new string[]
        {
            "FSDTarget"
        };
    }
}
