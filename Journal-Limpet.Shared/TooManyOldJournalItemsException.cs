using System;

namespace Journal_Limpet.Shared
{
    public class TooManyOldJournalItemsException : Exception
    {
        DateTime JournalDate { get; }
        Guid UserIdentifier { get; }

        public TooManyOldJournalItemsException(DateTime journalDate, Guid userIdentifier)
        {
            JournalDate = journalDate;
            UserIdentifier = userIdentifier;
        }
    }
}
