using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Mail;
using System;

namespace HOK.Elastic.FileSystemCrawler.WebAPI
{
    public class EmailService : IEmailService
    {
        private bool isDebug, isInfo, isWarn, isError;
        private Microsoft.Extensions.Logging.ILogger _logger;
        private string _host;
        private int _port;
        public string _defaultSender;
        public string DefaultSender => _defaultSender;

        public EmailService(Microsoft.Extensions.Logging.ILogger logger, string host, int port, string defaultSender)
        {
            isDebug = logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);
            isInfo = logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information);
            isWarn = logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning);
            isError = logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error);
            _logger = logger;
            if (isInfo) _logger.LogInformation("Initialized");
            _host = host;
            _port = port;
            _defaultSender = defaultSender;
        }

        //not recommended but can...https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient?view=net-7.0
        public void Send(MailMessage mailMessage)
        {
            Send(new MailMessage[] { mailMessage });
        }

        public void Send(IEnumerable<MailMessage> mailMessages)
        {
            var counter = 0;
            using (SmtpClient smtp = new SmtpClient())
            {
                smtp.Host = _host;
                smtp.Port = _port;
                foreach (var mailMessage in mailMessages)
                {
                    try
                    {
                        smtp.Send(mailMessage);
                        counter++;
                    }
                    catch (Exception ex)
                    {
                        if (isError) _logger.LogError("Error sending email because" + ex.Message);
                    }
                }
            }
            if (isInfo) _logger.LogInformation($"Sent {counter} emails");
        }

        public static MailMessage MakeMessage(string from, string to, string subject, string body)
        {
            MailMessage message = new MailMessage(from, to);
            message.Body = body;
            message.Subject = subject;
            return message;
        }
    }
}
