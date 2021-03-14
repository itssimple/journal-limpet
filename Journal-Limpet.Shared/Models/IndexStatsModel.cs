using System.Data;

namespace Journal_Limpet.Shared.Models
{
    public class IndexStatsModel
    {
        public long TotalUserCount { get; }
        public long TotalUserJournalCount { get; }
        public long TotalUserJournalLines { get; }
        public long TotalStarSystemCount { get; }

        public IndexStatsModel(DataRow row)
        {
            TotalUserCount = row.Field<long>("user_count");
            TotalUserJournalCount = row.Field<long>("journal_count");
            TotalUserJournalLines = row.Field<long>("total_number_of_lines");
            TotalStarSystemCount = row.Field<long>("total_star_systems");
        }
    }
}
