using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs.SharedCode
{
    public class SendLoginNotificationMethod
    {
        public static async Task SendLoginNotification(MSSQLDB db, IConfiguration configuration, Profile user)
        {
            var email = @"Hi there!

This is an automated email, since you have logged in to Journal Limpet at least once.

I'm sorry that I have to send you this email, but we need you to log in to Journal-Limpet <https://journal-limpet.com> again.

.. at least if you want us to be able to continue:
- Storing your Elite: Dangerous journals
- Sync progress with other applications

And if you don't want us to sync your account any longer, we'll delete your account after 6 months from your last fetched journal.

This is the only email we will ever send you (every time that you need to login)

Regards,
NoLifeKing85
Journal Limpet";

            var sendgridClient = new SendGridClient(configuration["SendGrid:ApiKey"]);
            var mail = MailHelper.CreateSingleEmail(
                new EmailAddress("no-reply+account-notifications@journal-limpet.com", "Journal Limpet"),
                new EmailAddress(user.NotificationEmail),
                "Login needed for further journal storage",
                email,
               $"<html><head></head><body>{email.Replace("\n", "<br />\n")}</body></html>"
            );

            await sendgridClient.SendEmailAsync(mail);

            await db.ExecuteNonQueryAsync("UPDATE user_profile SET last_notification_mail = GETUTCDATE() WHERE user_identifier = @userIdentifier",
                new SqlParameter("@userIdentifier", user.UserIdentifier)
            );
        }
    }
}
