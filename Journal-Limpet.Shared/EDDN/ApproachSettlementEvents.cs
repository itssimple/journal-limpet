using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class ApproachSettlement : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/approachsettlement/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);

        public enum AllowedEvents
        {
            ApproachSettlement
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            StarSystem,
            StarPos,
            SystemAddress,
            Name,
            BodyID,
            BodyName,
            Latitude,
            Longitude
        }

        public enum RemoveProperties
        {
            StationAllegiance,
            FactionState
        }
    }
}
