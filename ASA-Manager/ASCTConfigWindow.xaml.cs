using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MessageBox = System.Windows.Forms.MessageBox;

namespace ARKServerCreationTool
{
    /// <summary>
    /// Interaction logic for ASCTConfigWindow.xaml
    /// </summary>
    public partial class ASCTConfigWindow : Window
    {
        private bool firstLaunch = false;

        public ASCTConfigWindow(bool firstLaunch = false)
        {
            InitializeComponent();

            txt_InstallPath.Text = ASCTGlobalConfig.Instance.ServersInstallationPath;
            txt_defaultPort.Text = ASCTGlobalConfig.Instance.StartingGamePort.ToString();
            txt_portIncrement.Text = ASCTGlobalConfig.Instance.PortIncrement.ToString();
            chk_autoFirewallRules.IsChecked = ASCTGlobalConfig.Instance.AutomaticallyCreateFirewallRule;
            chk_AllowAutoLaunch.IsChecked = ASCTGlobalConfig.Instance.AllowAutomaticStart;
            chk_PromptStartAllServers.IsChecked = ASCTGlobalConfig.Instance.PromptStartAllServersInCluster;
            txt_clusterDir.Text = ASCTGlobalConfig.Instance.GlobalClusterDir;
            txt_curseforgeKey.Text = ASCTGlobalConfig.Instance.CurseForgeApiKey;

            this.firstLaunch = firstLaunch;
        }

        public ASCTConfigWindow() : this(false)
        {
        }

        private void btn_saveConfig_Click(object sender, RoutedEventArgs e)
        {
            ASCTGlobalConfig.Instance.ServersInstallationPath = txt_InstallPath.Text.Trim();
            ASCTGlobalConfig.Instance.StartingGamePort = ushort.Parse(txt_defaultPort.Text.Trim());
            ASCTGlobalConfig.Instance.PortIncrement = ushort.Parse(txt_portIncrement.Text.Trim());
            ASCTGlobalConfig.Instance.AutomaticallyCreateFirewallRule = chk_autoFirewallRules.IsChecked.Value;
            ASCTGlobalConfig.Instance.GlobalClusterDir = txt_clusterDir.Text;
            ASCTGlobalConfig.Instance.AllowAutomaticStart = chk_AllowAutoLaunch.IsChecked.Value;
            ASCTGlobalConfig.Instance.PromptStartAllServersInCluster = chk_PromptStartAllServers.IsChecked.Value;
            ASCTGlobalConfig.Instance.CurseForgeApiKey = txt_curseforgeKey.Text.Trim();
            ASCTGlobalConfig.Instance.Save();

            ASCTTools.FindOrCreateWindow<ServerList>();

            if (firstLaunch)
            {
                //UpdaterWindow update = new UpdaterWindow(true);

                //update.Show();
            }

            firstLaunch = false;

            this.Close();
        }

        private void WindowClose(object sender, CancelEventArgs e)
        {
            if (firstLaunch)
            {
                System.Windows.Forms.DialogResult result = MessageBox.Show(
                    "Are you sure you wish to exit, no config has been created?", "Are you sure?",
                    System.Windows.Forms.MessageBoxButtons.YesNo);

                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    Environment.Exit(0);
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }


        private void btn_gameDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.InitialDirectory = txt_InstallPath.Text;

                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    txt_InstallPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[0-9]+");

            e.Handled = !regex.IsMatch(e.Text);
        }

        private void btn_clusterDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.InitialDirectory = txt_clusterDir.Text;

                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    txt_clusterDir.Text = dialog.SelectedPath;
                }
            }
        }

        private async void btn_verifyKey_Click(object sender, RoutedEventArgs e)
        {
            string key = txt_curseforgeKey.Text.Trim();
            if (string.IsNullOrEmpty(key)) { MessageBox.Show("Enter an API key first."); return; }

            btn_verifyKey.IsEnabled = false;
            object? previous = btn_verifyKey.Content;
            btn_verifyKey.Content = "Checking…";
            try
            {
                var (ok, message) = await AppServices.CurseForge(key).ValidateKeyAsync();
                MessageBox.Show(message, ok ? "API key valid" : "API key check");
            }
            finally
            {
                btn_verifyKey.Content = previous;
                btn_verifyKey.IsEnabled = true;
            }
        }
    }
}