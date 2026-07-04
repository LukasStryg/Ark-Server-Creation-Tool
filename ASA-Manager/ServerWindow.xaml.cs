using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using static ARKServerCreationTool.ASCTTools;
using System.Diagnostics.Metrics;
using System.Windows.Media.Animation;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Servers;

namespace ARKServerCreationTool
{
    /// <summary>
    /// Interaction logic for ServerWindow.xaml
    /// </summary>
    public partial class ServerWindow : Window
    {
        public int targetServerID { get; private set; }

        private ASCTServerConfig targetServer
        {
            get
            {
                return ASCTGlobalConfig.Instance.Servers.Where(s => s.ID == targetServerID).First();
            }
        }

        private ASCTGlobalConfig config
        {
            get
            {
                return ASCTGlobalConfig.Instance;
            }
        }

        private GameProcessManager processManager { get { return GameProcessManager.GetGameProcessManager(targetServerID); } }

        public ServerWindow(int server) : base()
        {
            targetServerID = server;

            InitializeComponent();

            controlProgress = new Progress<string>(msg => lbl_controlStatus.Text = msg);

            lbl_serverName.Content = $"{targetServer.Name}";
            lbl_serverCluster.Content = $"{(targetServer.ClusterKey != string.Empty ? targetServer.ClusterKey : "Not Clustered")}";

            UpdateStatus();
        }

        RunButtonStatus buttonStatus = RunButtonStatus.Unknown;

        private readonly Progress<string> controlProgress;

        private void UpdateStatus()
        {
            bool isServerClustered = targetServer.ClusterKey != string.Empty;

            chk_entireCluster.IsEnabled = isServerClustered;
            if (!isServerClustered) chk_entireCluster.IsChecked = false;

            bool actOnCluster = chk_entireCluster.IsChecked.Value;
            btn_start.Content = actOnCluster ? "Start Cluster" : "Start Server";
            btn_stop.Content = actOnCluster ? "Stop Cluster" : "Stop Server";

            bool isRunning = targetServer.IsRunning;

            bool canStart = false;
            bool canStop = false;

            var cluster = config.Servers.Where(s => s.ClusterKey == targetServer.ClusterKey);

            if (isServerClustered && actOnCluster)
            {
                canStart = cluster.Any(s => !s.IsRunning);
                canStop = cluster.Any(s => s.IsRunning);
            }
            else
            {
                canStop = isRunning;
                canStart = !isRunning;
            }

            btn_start.IsEnabled = canStart;
            btn_stop.IsEnabled = canStop;
            lbl_serverName.Content = $"Server: {targetServer.Name}";
            lbl_clusterStatus.Visibility = isServerClustered ? Visibility.Visible : Visibility.Collapsed;
            lbl_serverStatus.Content = $"Server Status: {(isRunning ? "Running" : "Stopped")}";
            if (isServerClustered)
            {
                lbl_clusterStatus.Content = $"Cluster Status: {cluster.Count(s => s.IsRunning)}/{cluster.Count()} Running";
            }
            this.Title = $"ASCT Server - {targetServer.Name}";
        }

        private void btn_openConfig_Click(object sender, RoutedEventArgs e)
        {
            ASCTTools.FindOrCreateServerConfigWindow(targetServerID);
        }

        private void btn_openUpdater_Click(object sender, RoutedEventArgs e)
        {
            ASCTTools.FindOrCreateServerUpdaterWindow(targetServerID);
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            UpdateStatus();
        }

        private void btn_openGUSConfig_Click(object sender, RoutedEventArgs e)
        {
            string GUSConfigPath = Path.Combine(targetServer.GameDirectory, @"ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini");

            if (File.Exists(GUSConfigPath))
            {
                Process.Start("notepad.exe", GUSConfigPath).WaitForExit();
            }
            else
            {
                string message = "No gameusersettings.ini file currently exists. Would you like to create one from the template?";
                string caption = "Missing Config";
                MessageBoxResult result = MessageBox.Show(message, caption,
                              MessageBoxButton.YesNo );

                if (result == MessageBoxResult.Yes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(GUSConfigPath));
                    File.WriteAllText(GUSConfigPath, Properties.Resources.GUSConfigTemplate);
                    Process.Start("notepad.exe", GUSConfigPath).WaitForExit();
                }
            }
        }

        private void btn_openGameINIConfig_Click(object sender, RoutedEventArgs e)
        {
            string GameConfigPath = Path.Combine(targetServer.GameDirectory, @"ShooterGame\Saved\Config\WindowsServer\Game.ini");

            if (File.Exists(GameConfigPath))
            {
                Process.Start("notepad.exe", GameConfigPath).WaitForExit();
            }
            else
            {
                string message = "No game.ini file currently exists. Would you like to create one from the template?";
                string caption = "Missing Config";
                MessageBoxResult result = MessageBox.Show(message, caption,
                              MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(GameConfigPath));
                    File.WriteAllText(GameConfigPath, Properties.Resources.GameConfigTemplate);
                    Process.Start("notepad.exe", GameConfigPath).WaitForExit();
                }
            }
        }

        private void btn_start_Click(object sender, RoutedEventArgs e)
        {
            bool actOnCluster = chk_entireCluster.IsChecked.Value;

            if (actOnCluster)
            {
                StartStopEntireCluster(targetServer.ClusterKey, true);
            }
            else
            {
                targetServer.ProcessManager.Start();
                targetServer.SnapshotRunningModVersions(AppServices.MetadataCache);
                config.Save();
            }

            UpdateStatus();
        }

        private void btn_stop_Click(object sender, RoutedEventArgs e)
        {
            bool actOnCluster = chk_entireCluster.IsChecked.Value;

            if (actOnCluster)
            {
                StartStopEntireCluster(targetServer.ClusterKey, false);
            }
            else
            {
                targetServer.ProcessManager.Stop();
            }

            UpdateStatus();
        }

        private void StartStopEntireCluster(string clusterKey, bool start) //set start to true to start all servers in cluster, false to stop them
        {
            var cluster = config.Servers.Where(s => s.ClusterKey == clusterKey);

            Parallel.ForEach(cluster.Where(s => s.IsRunning != start), s =>
            {
                if (start)
                {
                    s.ProcessManager.Start();
                }
                else
                {
                    s.ProcessManager.Stop();
                }
            });
        }

        private void chk_entireCluster_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus();
        }

        private ServerControlService BuildControlService()
        {
            var adapter = new GameProcessControllerAdapter(processManager);
            return new ServerControlService(
                () => new RconClient("127.0.0.1", targetServer.RconPort),
                adapter,
                targetServer.ServerAdminPassword);
        }

        private async void btn_gracefulStop_Click(object sender, RoutedEventArgs e)
        {
            btn_gracefulStop.IsEnabled = false;
            try
            {
                var result = await BuildControlService().GracefulStopAsync(TimeSpan.FromSeconds(60), controlProgress);
                lbl_controlStatus.Text = $"Stop result: {result}";
            }
            finally { btn_gracefulStop.IsEnabled = true; UpdateStatus(); }
        }

        private async void btn_restartApply_Click(object sender, RoutedEventArgs e)
        {
            btn_restartApply.IsEnabled = false;
            try
            {
                var client = AppServices.CurseForge();
                if (client.HasKey)
                {
                    try
                    {
                        await AppServices.MetadataCache.RefreshAsync(targetServer.Mods.Select(m => m.ProjectId), client, DateTimeOffset.UtcNow);
                        AppServices.MetadataCache.Save();
                    }
                    catch (Exception ex) { lbl_controlStatus.Text = $"Metadata refresh failed: {ex.Message}"; }
                }

                bool ok = await BuildControlService().RestartToApplyAsync(TimeSpan.FromSeconds(60), controlProgress);
                if (ok)
                {
                    targetServer.SnapshotRunningModVersions(AppServices.MetadataCache);
                    config.Save();
                }
                lbl_controlStatus.Text = ok ? "Restarted; mods re-download on boot." : "Restart failed; check the server.";
            }
            finally { btn_restartApply.IsEnabled = true; UpdateStatus(); }
        }
    }
}
