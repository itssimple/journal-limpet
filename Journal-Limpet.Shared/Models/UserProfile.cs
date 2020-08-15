using System;
using System.Data;

namespace Journal_Limpet.Shared.Models
{
    public class UserProfile
    {
        public Guid UserIdentifier { get; set; }
        public DateTimeOffset Created { get; set; }
        public bool Deleted { get; set; }
        public DateTimeOffset? DeletionDate { get; set; }
        public object UserSettings { get; set; }

        public UserProfile(DataRow row)
        {
            UserIdentifier = row.Field<Guid>("user_identifier");
            Created = new DateTimeOffset(row.Field<DateTime>("created"), TimeSpan.Zero);
            Deleted = row.Field<bool>("deleted");
            DeletionDate = (!row.IsNull("deletion_date") ? new DateTimeOffset(row.Field<DateTime>("deletion_date"), TimeSpan.Zero) as DateTimeOffset? : null);
            UserSettings = row.Field<string>("user_settings");
        }
    }
}
