using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class Journal : EventBase
    {
        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);

        public override string SchemaRef() => "https://eddn.edcd.io/schemas/journal/1";

        public enum AllowedEvents
        {
            Docked,
            FSDJump,
            Scan,
            Location,
            SAASignalsFound,
            CarrierJump
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            StarSystem,
            StarPos,
            SystemAddress
        }

        public enum RemoveProperties
        {
            Wanted,
            ActiveFine,
            CockpitBreach,
            BoostUsed,
            FuelLevel,
            FuelUsed,
            JumpDist,
            Latitude,
            Longitude,
            HappiestSystem,
            HomeSystem,
            MyReputation,
            SquadronFaction,
            IsNewEntry,
            NewTraitsDiscovered,
            Traits,
            VoucherAmount
        }
    }
}
