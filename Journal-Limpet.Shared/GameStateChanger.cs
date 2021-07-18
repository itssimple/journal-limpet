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
                gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
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
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();
                gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                gameState.BodyName = elementAsDictionary["Body"].GetString();
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

                    gameState.SystemName = elementAsDictionary["StarSystem"].GetString();
                }

                if (elementAsDictionary.ContainsKey("SystemAddress"))
                {
                    gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                }

                gameState.BodyName = null;
                gameState.BodyId = null;
            }

            if (eventName == "ShipLockerMaterials")
            {
                gameState.Odyssey = true;
            }

            if (eventName == "SupercruiseExit")
            {
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();

                if (elementAsDictionary.ContainsKey("Body"))
                {
                    gameState.BodyName = elementAsDictionary["Body"].GetString();
                }
                else
                {
                    gameState.BodyName = null;
                }

                if (elementAsDictionary.ContainsKey("BodyID"))
                {
                    gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                }
                else
                {
                    gameState.BodyId = null;
                }
            }

            if (new[] { "Location", "FSDJump", "Docked", "CarrierJump"/*, "Touchdown", "Liftoff"*/ }.Contains(eventName))
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
                    if (elementAsDictionary.ContainsKey("SystemAddress"))
                    {
                        gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                    }

                    gameState.SystemName = elementAsDictionary["StarSystem"].GetString();

                    if (elementAsDictionary.ContainsKey("StarPos"))
                    {
                        gameState.SystemCoordinates = elementAsDictionary["StarPos"];
                    }

                    if (elementAsDictionary.ContainsKey("Body"))
                    {
                        gameState.BodyName = elementAsDictionary["Body"].GetString();
                    }

                    if (elementAsDictionary.ContainsKey("BodyID"))
                    {
                        gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                    }
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

            if (new[] { "SAASignalsFound", "Scan", "FSSDiscoveryScan", "CodexEntry", "FSSAllBodiesFound", "SAAScanComplete" }.Contains(eventName))
            {
                if (elementAsDictionary.ContainsKey("SystemAddress"))
                {
                    gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                }

                if (elementAsDictionary.ContainsKey("StarSystem"))
                {
                    gameState.SystemName = elementAsDictionary["StarSystem"].GetString();
                }

                if (elementAsDictionary.ContainsKey("StarPos"))
                {
                    gameState.SystemCoordinates = elementAsDictionary["StarPos"];
                }

                if (elementAsDictionary.ContainsKey("BodyName"))
                {
                    gameState.BodyName = elementAsDictionary["BodyName"].GetString();
                }

                if (elementAsDictionary.ContainsKey("BodyID"))
                {
                    gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                }
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
