using System.Data;

namespace Journal_Limpet.Shared.Models
{
    public class IndexStatsModel
    {
        public long TotalUserCount { get; set; }
        public long TotalUserJournalCount { get; set; }
        public long TotalUserJournalLines { get; set; }

        public IndexStatsModel() { }

        public IndexStatsModel(DataRow row)
        {
            TotalUserCount = row.Field<long>("user_count");
            TotalUserJournalCount = row.Field<long>("journal_count");
            TotalUserJournalLines = row.Field<long>("total_number_of_lines");
        }
    }
}
