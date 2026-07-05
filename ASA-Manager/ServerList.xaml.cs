using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using static ARKServerCreationTool.ASCTTools;


namespace ARKServerCreationTool
{
    /// <summary>
    /// Interaction logic for ServerList.xaml
    /// </summary>
    public partial class ServerList : Window
    {
        ASCTGlobalConfig config = ASCTGlobalConfig.Instance;

        public ServerList()
        {
            InitializeComponent();

            IEnumerable<string> missingCerts = CertificateManagement.GetMissingCertificates();

            if (missingCerts.Any()) 
            {
                DialogResult result = System.Windows.Forms.MessageBox.Show(
                    "The following certificates are missing: " + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, missingCerts) + Environment.NewLine + Environment.NewLine + 
                    @"Some game features may not work without them, such as CurseForge mods. Would you like to install them?",
                    "Install Missing Certificates?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    foreach (string cert in missingCerts)
                    {
                        try
                        {
                            CertificateManagement.InstallCertByName(cert);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.Forms.MessageBox.Show(ex.ToString());
                            throw;
                        }
                    }
                }
            }

            UpdateList();
        }

        public void UpdateList()
        {
            Dispatcher.Invoke(() =>
            {
                dg_ServerList.ItemsSource = config.Servers;

                dg_ServerList.Items.Refresh();

                UpdateButtons();
            });
        }

        private void btn_mainConfig_Click(object sender, RoutedEventArgs e)
        {
            ASCTTools.FindOrCreateWindow<ASCTConfigWindow>();
        }

        private void dg_ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        RunButtonStatus runButtonStatus = RunButtonStatus.Unknown;

        private void UpdateButtons()
        {
            btn_run.IsEnabled = btn_deleteServer.IsEnabled = btn_viewServer.IsEnabled = dg_ServerList.SelectedItem != null;

            if (btn_run.IsEnabled)
            {
                if (!((ASCTServerConfig)dg_ServerList.SelectedItem).ProcessManager.IsRunning)
                {
                    btn_run.Content = "Start Server";
                    runButtonStatus = RunButtonStatus.Run;
                }
                else
                {
                    btn_run.Content = "Stop Server";
                    runButtonStatus = RunButtonStatus.Stop;
                }
            }
            else
            {
                btn_run.Content = "Select a server";
                runButtonStatus = RunButtonStatus.Unknown;
            }

            var serversRunning = config.Servers.Select(s => s.ProcessManager.IsRunning);

            btn_startAll.IsEnabled = serversRunning.Any(b => !b);
            btn_stopAll.IsEnabled = serversRunning.Any(b => b);
        }

        private void btn_viewServer_Click(object sender, RoutedEventArgs e)
        {
            ASCTTools.FindOrCreateServerWindow(((ASCTServerConfig)dg_ServerList.SelectedItem).ID);
        }

        private void btn_deleteServer_Click(object sender, RoutedEventArgs e)
        {
            var removeResult = System.Windows.MessageBox.Show("Are you sure you want to remove this server?", "Are you sure?", MessageBoxButton.YesNo);

            try
            {
                if (removeResult == MessageBoxResult.Yes)
                {
                    var deleteResult = System.Windows.MessageBox.Show("Would you like to permanently delete the files for the server?", "Delete Files?", MessageBoxButton.YesNoCancel);

                    if (deleteResult == MessageBoxResult.Cancel)
                    {
                        return; //Do nothing, user cancelled on the second prompt
                    }

                    string path = ((ASCTServerConfig)dg_ServerList.SelectedItem).GameDirectory;

                    config.Servers.Remove(((ASCTServerConfig)dg_ServerList.SelectedItem));

                    config.Save();

                    if (deleteResult == MessageBoxResult.Yes)
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                    }

                    UpdateList();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.ToString());
                throw;
            }
        }

        private void btn_addServer_Click(object sender, RoutedEventArgs e)
        {
            ASCTTools.FindOrCreateServerConfigWindow(null);
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            UpdateList();
        }

        private async void btn_RunServer_Click(object sender, RoutedEventArgs e)
        {
            if (runButtonStatus == RunButtonStatus.Unknown)
            {
                UpdateButtons(); 
                return;
            }

            try
            {
                ASCTServerConfig selectedServer = ((ASCTServerConfig)dg_ServerList.SelectedItem);
                bool selectedRunning = selectedServer.ProcessManager.IsRunning;

                var cluster = config.Servers.AsParallel().Where(s => s.ClusterKey == selectedServer.ClusterKey && selectedRunning == s.ProcessManager.IsRunning);

                bool launchOne = true; //Set to false if we act on the cluster

                if (selectedServer.ClusterKey != string.Empty && cluster.Count() > 1)
                {
                    MessageBoxResult result = MessageBoxResult.No;

                    if (config.PromptStartAllServersInCluster)
                    {
                        result = System.Windows.MessageBox.Show("Would you like to perform this action on all of the servers in the same cluster?", "", MessageBoxButton.YesNo);
                    }

                    if (result == MessageBoxResult.Yes)
                    {
                        ASCTServerConfig[] serversInCluster = cluster.ToArray();

                        if (runButtonStatus == RunButtonStatus.Run)
                        {
                            await AppServices.Coordinator.StartStaggeredAsync(serversInCluster, UpdateList);
                            config.Save();
                        }
                        else if (runButtonStatus == RunButtonStatus.Stop)
                        {
                            foreach (var s in serversInCluster) { AppServices.Coordinator.NotifyStopping(s.ID); s.TransientStatus = "Stopping…"; }
                            UpdateList();
                            await Services.Servers.ServerControl.GracefulStopManyAsync(serversInCluster);
                            foreach (var s in serversInCluster) { AppServices.Coordinator.NotifyStopped(s.ID); s.TransientStatus = null; }
                        }

                        launchOne = false;
                    }
                }

                if (launchOne)
                {
                    if (runButtonStatus == RunButtonStatus.Run)
                    {
                        await AppServices.Coordinator.StartStaggeredAsync(new List<ASCTServerConfig> { selectedServer }, UpdateList);
                        config.Save();
                    }
                    else if (runButtonStatus == RunButtonStatus.Stop)
                    {
                        AppServices.Coordinator.NotifyStopping(selectedServer.ID);
                        selectedServer.TransientStatus = "Stopping…";
                        UpdateList();
                        await Services.Servers.ServerControl.GracefulStopAsync(selectedServer);
                        AppServices.Coordinator.NotifyStopped(selectedServer.ID);
                        selectedServer.TransientStatus = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Operation failed: {ex.Message}");
            }
            finally
            {
                foreach (var s in config.Servers) s.TransientStatus = null;
                UpdateList();
            }
        }

        private async void btn_startAll_Click(object sender, RoutedEventArgs e)
        {
            await AppServices.Coordinator.StartStaggeredAsync(config.Servers, UpdateList);
            config.Save();
            UpdateList();
        }

        private async void btn_stopAll_Click(object sender, RoutedEventArgs e)
        {
            var stopping = config.Servers.Where(s => s.IsRunning).ToList();
            foreach (var s in stopping) { AppServices.Coordinator.NotifyStopping(s.ID); s.TransientStatus = "Stopping…"; }
            UpdateList();
            try
            {
                await Services.Servers.ServerControl.GracefulStopManyAsync(config.Servers);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Stop all failed: {ex.Message}");
            }
            finally
            {
                foreach (var s in stopping) { AppServices.Coordinator.NotifyStopped(s.ID); s.TransientStatus = null; }
                UpdateList();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Application.Current.Windows.Count > 1)
            {
                System.Windows.Forms.MessageBox.Show("This window can not be closed while others are still open");
                e.Cancel = true;
            }
        }

        private void btn_openUpdater_Click(object sender, RoutedEventArgs e)
        {
            if (dg_ServerList.SelectedItem == null)
            {
                FindOrCreateServerUpdaterWindow();
            }
            else
            {
                FindOrCreateServerUpdaterWindow(((ASCTServerConfig)dg_ServerList.SelectedItem).ID);
            }
        }
    }
}



