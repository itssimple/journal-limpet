using System;

namespace Journal_Limpet.Shared.EDDN
{
    public abstract class EventBase
    {
        public abstract string SchemaRef();

        public abstract Type GetAllowedEvents();
        public abstract Type GetRequiredProperties();
        public abstract Type GetRemoveProperties();
    }
}
