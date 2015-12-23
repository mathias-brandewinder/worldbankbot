using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBankBot;
using Topshelf;

namespace WorldBankBotService
{
    class Program
    {
        static void Main(string[] args)
        {
            HostFactory.Run(x =>
            {
                x.Service<Bot>(service =>
                {
                    service.ConstructUsing(name => new Bot());
                    service.WhenStarted(bot => bot.Start());
                    service.WhenStopped(bot => bot.Stop());
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(1);
                });

                x.RunAsLocalSystem();
                
                x.SetDescription("WorldBank Twitter Bot");
                x.SetDisplayName("WorldBankBot");
                x.SetServiceName("WorldBankBot");
            });
        }
    }
}
