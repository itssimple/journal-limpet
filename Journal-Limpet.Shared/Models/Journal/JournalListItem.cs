using System;
using System.Data;

namespace Journal_Limpet.Shared.Models.Journal
{
    public class JournalListItem
    {
        public long JournalId { get; set; }
        public string JournalDate { get; set; }
        public string CompleteEntry { get; set; }
        public long NumberOfLines { get; set; }

        public JournalListItem(DataRow row)
        {
            JournalId = row.Field<long>("journal_id");
            JournalDate = new DateTimeOffset(row.Field<DateTime>("journal_date"), TimeSpan.Zero).Date.ToString("yyyy-MM-dd");
            CompleteEntry = row.Field<bool>("complete_entry") ? "Yes" : "No";
            NumberOfLines = row.Field<long>("last_processed_line_number");
        }
    }
}
