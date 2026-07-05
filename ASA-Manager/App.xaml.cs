using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ARKServerCreationTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            bool newConfig = false;

            if (newConfig = (ASCTGlobalConfig.Instance == null))
            {
                ASCTGlobalConfig.NewConfig();
                ASCTConfiguration legacyConfig = ASCTConfiguration.LoadConfig();
                if (legacyConfig != null)
                {
                    ASCTGlobalConfig.Instance.AutomaticallyCreateFirewallRule = legacyConfig.AutomaticallyCreateFirewallRule;

                    ASCTServerConfig legacyServer = new ASCTServerConfig(ASCTGlobalConfig.Instance.NextAvailableID(), ASCTGlobalConfig.Instance.NextAvailablePort());
                    legacyServer.IPAddress = legacyConfig.IPAddress;
                    legacyServer.GamePort = legacyConfig.GamePort;
                    legacyServer.GameDirectory = legacyConfig.GameDirectory;
                    legacyServer.customLaunchArgs = legacyConfig.customLaunchArgs;
                    legacyServer.useCustomLaunchArgs = legacyConfig.useCustomLaunchArgs;
                    legacyServer.UseMultihome = legacyConfig.UseMultihome;

                    ASCTGlobalConfig.Instance.Servers.Add(legacyServer);

                    MessageBox.Show("Existing settings have been imported to the new format successfully");
                }
            }

            if (newConfig)
            {
                ASCTConfigWindow configWindow = new ASCTConfigWindow(true);
                configWindow.Show();
            }
            else
            {
                ServerList list = new ServerList();

                list.Show();

                AppServices.Coordinator.Start();

                if (ASCTGlobalConfig.Instance.AllowAutomaticStart)
                {
                    var autoStart = ASCTGlobalConfig.Instance.Servers
                        .Where(s => s.StartAutomatically && !s.ExcludeFromBulkStart);
                    await AppServices.Coordinator.StartStaggeredAsync(autoStart);
                }
            }
        }
    }
}
