using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Mods;

namespace ARKServerCreationTool
{
    /// <summary>Pushes a source server's mod list to one or more target servers (or a whole cluster).</summary>
    public partial class CopyModsToServersWindow : Window
    {
        public class TargetRow : INotifyPropertyChanged
        {
            public int ServerId { get; init; }
            public string ClusterKey { get; init; } = string.Empty;
            public string Display { get; init; } = string.Empty;
            private bool _selected;
            public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ASCTServerConfig sourceServer;
        private readonly List<ModEntry> sourceMods;
        private readonly ObservableCollection<TargetRow> rows = new();

        public CopyModsToServersWindow(ASCTServerConfig sourceServer, IEnumerable<ModEntry> sourceMods)
        {
            InitializeComponent();
            this.sourceServer = sourceServer;
            this.sourceMods = sourceMods.ToList();

            foreach (var s in ASCTGlobalConfig.Instance.Servers.Where(s => s.ID != sourceServer.ID))
            {
                string cluster = string.IsNullOrEmpty(s.ClusterKey) ? "no cluster" : s.ClusterKey;
                rows.Add(new TargetRow
                {
                    ServerId = s.ID,
                    ClusterKey = s.ClusterKey,
                    Display = $"{s.Name}  ({cluster}){(s.IsRunning ? "  [running]" : "")}"
                });
            }
            lst_targets.ItemsSource = rows;
        }

        private void btn_selectCluster_Click(object sender, RoutedEventArgs e)
        {
            string srcCluster = sourceServer.ClusterKey;
            foreach (var r in rows) r.Selected = !string.IsNullOrEmpty(srcCluster) && r.ClusterKey == srcCluster;
        }

        private void btn_selectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in rows) r.Selected = true;
        }

        private void btn_apply_Click(object sender, RoutedEventArgs e)
        {
            var targets = rows.Where(r => r.Selected).ToList();
            if (targets.Count == 0) { MessageBox.Show("No target servers selected."); return; }

            bool includeDisabled = chk_includeDisabled.IsChecked == true;
            bool replace = rb_replace.IsChecked == true;
            bool anyRunning = false;

            foreach (var t in targets)
            {
                var server = ASCTGlobalConfig.Instance.Servers.First(s => s.ID == t.ServerId);
                server.Mods = replace
                    ? ModListOps.Replace(sourceMods, includeDisabled)
                    : ModListOps.Merge(server.Mods, sourceMods, includeDisabled);
                anyRunning |= server.IsRunning;
            }

            ASCTGlobalConfig.Instance.Save();
            MessageBox.Show(anyRunning
                ? "Mods copied. Running targets will apply the change on their next restart."
                : "Mods copied.");
            Close();
        }

        private void btn_cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
