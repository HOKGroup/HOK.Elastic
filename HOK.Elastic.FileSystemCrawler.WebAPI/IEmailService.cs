using System.Collections.Generic;
using System.Net.Mail;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public interface IEmailService
    {
        string DefaultSender { get; }

        void Send(IEnumerable<MailMessage> mailMessages);
        void Send(MailMessage mailMessage);
    }
}