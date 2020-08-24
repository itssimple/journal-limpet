using System;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.User
{
    public class Profile
    {
        public Guid UserIdentifier { get; set; }
        public DateTimeOffset Created { get; set; }
        public bool Deleted { get; set; }
        public DateTimeOffset? DeletionDate { get; set; }
        [JsonIgnore]
        public Settings UserSettings { get; set; }

        public Profile(DataRow row)
        {
            UserIdentifier = row.Field<Guid>("user_identifier");
            Created = new DateTimeOffset(row.Field<DateTime>("created"), TimeSpan.Zero);
            Deleted = row.Field<bool>("deleted");
            DeletionDate = (!row.IsNull("deletion_date") ? new DateTimeOffset(row.Field<DateTime>("deletion_date"), TimeSpan.Zero) as DateTimeOffset? : null);
            UserSettings = JsonSerializer.Deserialize<Settings>(row.Field<string>("user_settings"));
        }
    }
}
