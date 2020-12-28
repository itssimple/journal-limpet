using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared
{
    public static class MailSender
    {
        public static async Task SendSingleEmail(IConfiguration configuration, string fromAddress, string subject, string mailBody, string mailBodyHtml = null)
        {
            if (string.IsNullOrWhiteSpace(mailBodyHtml))
            {
                mailBodyHtml = mailBody.Replace("\n", "<br />\n");
            }

            var sendgridClient = new SendGridClient(configuration["SendGrid:ApiKey"]);
            var mail = MailHelper.CreateSingleEmail(
                new EmailAddress(fromAddress),
                new EmailAddress(configuration["ErrorMail"]),
                subject,
                mailBody,
                mailBodyHtml
            );

            await sendgridClient.SendEmailAsync(mail);
        }
    }
}
