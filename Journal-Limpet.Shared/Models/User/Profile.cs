using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.User
{
    public class Profile
    {
        public Guid UserIdentifier { get; }
        public DateTimeOffset Created { get; }
        public bool Deleted { get; }
        public DateTimeOffset? DeletionDate { get; }
        [JsonIgnore]
        public Settings UserSettings { get; set; }
        public string NotificationEmail { get; }
        public DateTimeOffset? LastNotificationMail { get; }

        public bool SendToEDDN { get; }
        public bool SkipDownload { get; }
        [JsonIgnore]
        public Dictionary<string, JsonElement> IntegrationSettings { get; }

        public Profile(DataRow row)
        {
            UserIdentifier = row.Field<Guid>("user_identifier");
            Created = new DateTimeOffset(row.Field<DateTime>("created"), TimeSpan.Zero);
            Deleted = row.Field<bool>("deleted");
            DeletionDate = (!row.IsNull("deletion_date") ? new DateTimeOffset(row.Field<DateTime>("deletion_date"), TimeSpan.Zero) as DateTimeOffset? : null);
            UserSettings = JsonSerializer.Deserialize<Settings>(row.Field<string>("user_settings"));
            NotificationEmail = row.Field<string>("notification_email");
            LastNotificationMail = (!row.IsNull("last_notification_mail") ? new DateTimeOffset(row.Field<DateTime>("last_notification_mail"), TimeSpan.Zero) as DateTimeOffset? : null);
            SendToEDDN = row.Field<bool>("send_to_eddn");
            SkipDownload = row.Field<bool>("skip_download");
            IntegrationSettings = !row.IsNull("integration_settings") ?
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row["integration_settings"].ToString()) :
                new Dictionary<string, JsonElement>();
        }
    }
}
