using System;
using System.Data;

namespace Journal_Limpet.Shared.Models
{
    public class UnsentJournalInfo
    {
        public Guid UserIdentifier { get; set; }
        public long JournalCount { get; set; }

        public UnsentJournalInfo(DataRow row)
        {
            UserIdentifier = row.Field<Guid>("user_identifier");
            JournalCount = row.Field<long>("journal_count");
        }
    }
}
