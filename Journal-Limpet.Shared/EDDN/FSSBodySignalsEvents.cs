using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class FSSBodySignals : EventBase
    {
        public override string SchemaRef() => "https://eddn.edcd.io/schemas/fssbodysignals/1";

        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);
        public enum AllowedEvents
        {
            FSSBodySignals
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            StarSystem,
            StarPos,
            SystemAddress,
            BodyID,
            Signals
        }

        public enum RemoveProperties
        {
        }
    }
}
