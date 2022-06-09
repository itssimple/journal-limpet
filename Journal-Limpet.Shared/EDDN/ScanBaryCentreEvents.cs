using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class ScanBaryCentre : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/scanbarycentre/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);

        public enum AllowedEvents
        {
            ScanBaryCentre
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            StarSystem,
            StarPos,
            SystemAddress,
            BodyID
        }

        public enum RemoveProperties
        {
        }
    }
}
