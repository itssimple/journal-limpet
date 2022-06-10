using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class FSSDiscoveryScan : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/fssdiscoveryscan/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);
        public enum AllowedEvents
        {
            FSSDiscoveryScan
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            SystemName,
            StarPos,
            SystemAddress,
            BodyCount,
            NonBodyCount
        }

        public enum RemoveProperties
        {
            Progress
        }
    }
}
