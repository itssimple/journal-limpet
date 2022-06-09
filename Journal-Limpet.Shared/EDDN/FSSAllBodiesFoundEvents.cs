using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class FSSAllBodiesFound : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/fssallbodiesfound/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);
        public enum AllowedEvents
        {
            FSSAllBodiesFound
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            SystemName,
            StarPos,
            SystemAddress,
            Count
        }

        public enum RemoveProperties
        {
        }
    }
}
