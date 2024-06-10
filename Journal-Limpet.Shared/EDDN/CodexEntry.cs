using System;

namespace Journal_Limpet.Shared.EDDN
{
    public class CodexEntry : EventBase
    {
        public override Type GetAllowedEvents() => typeof(AllowedEvents);

        public override Type GetRemoveProperties() => typeof(RemoveProperties);

        public override Type GetRequiredProperties() => typeof(RequiredProperties);

        public override string SchemaRef() => "https://eddn.edcd.io/schemas/codexentry/1";

        public enum AllowedEvents
        {
            CodexEntry
        }

        public enum RequiredProperties
        {
            timestamp,
            @event,
            System,
            StarPos,
            SystemAddress,
            EntryID
        }

        public enum RemoveProperties
        {
            IsNewEntry,
            NewTraitsDiscovered,
            BodyName
        }
    }
}
