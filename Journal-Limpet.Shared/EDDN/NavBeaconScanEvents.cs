using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class NavBeaconScan : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/navbeaconscan/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);
        public enum AllowedEvents
        {
            NavBeaconScan
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            StarSystem,
            StarPos,
            SystemAddress,
            NumBodies
        }

        public enum RemoveProperties
        {
        }
    }
}
