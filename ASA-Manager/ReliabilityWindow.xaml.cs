using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ARKServerCreationTool.Models;

namespace ARKServerCreationTool
{
    /// <summary>Global reliability settings + scheduled-task editor.</summary>
    public partial class ReliabilityWindow : Window
    {
        private readonly ASCTGlobalConfig config = ASCTGlobalConfig.Instance;

        private sealed class TargetItem
        {
            public ScheduleTargetKind Kind;
            public int? ServerId;
            public string? ClusterKey;
            public string Label = "";
            public override string ToString() => Label;
        }

        public ReliabilityWindow()
        {
            InitializeComponent();

            txt_stagger.Text = config.AutoStartStaggerTime.ToString();
            txt_backupRoot.Text = config.BackupRoot;
            txt_keep.Text = config.BackupKeepCount.ToString();
            chk_crashRestart.IsChecked = config.CrashAutoRestartEnabled;
            txt_crashThreshold.Text = config.CrashThresholdCount.ToString();
            txt_crashWindow.Text = config.CrashWindowMinutes.ToString();

            cmb_type.ItemsSource = Enum.GetValues(typeof(ScheduledTaskType));
            cmb_type.SelectedIndex = 0;
            cmb_target.ItemsSource = BuildTargets();
            cmb_target.SelectedIndex = 0;
            cmb_mode.ItemsSource = Enum.GetValues(typeof(ScheduleMode));
            cmb_mode.SelectedIndex = 0;

            RefreshTasks();
        }

        private List<TargetItem> BuildTargets()
        {
            var items = new List<TargetItem> { new TargetItem { Kind = ScheduleTargetKind.All, Label = "All servers" } };
            foreach (var key in config.Servers.Select(s => s.ClusterKey).Where(k => !string.IsNullOrEmpty(k)).Distinct())
                items.Add(new TargetItem { Kind = ScheduleTargetKind.Cluster, ClusterKey = key, Label = $"Cluster: {key}" });
            foreach (var s in config.Servers)
                items.Add(new TargetItem { Kind = ScheduleTargetKind.Server, ServerId = s.ID, Label = $"Server: {s.Name}" });
            return items;
        }

        private void RefreshTasks()
        {
            // Summaries stay index-aligned with config.ScheduledTasks so removal by index is correct.
            lst_tasks.ItemsSource = null;
            lst_tasks.ItemsSource = config.ScheduledTasks.Select(Summarize).ToList();
        }

        private static string Summarize(ScheduledTask t)
        {
            string target = t.TargetKind switch
            {
                ScheduleTargetKind.All => "All",
                ScheduleTargetKind.Cluster => $"Cluster {t.TargetClusterKey}",
                _ => $"Server #{t.TargetServerId}",
            };
            string when = t.Mode == ScheduleMode.DailyAtTime ? $"Daily {t.DailyTime:hh\\:mm}" : $"Every {t.IntervalHours}h";
            return $"{(t.Enabled ? "" : "(off) ")}{t.Type} • {target} • {when}";
        }

        private void cmb_mode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (txt_when == null || cmb_mode.SelectedItem == null) return;
            txt_when.Text = (ScheduleMode)cmb_mode.SelectedItem == ScheduleMode.DailyAtTime ? "05:00" : "6";
        }

        private void btn_addTask_Click(object sender, RoutedEventArgs e)
        {
            if (cmb_target.SelectedItem is not TargetItem target) return;
            var mode = (ScheduleMode)cmb_mode.SelectedItem;

            var task = new ScheduledTask
            {
                Id = config.ScheduledTasks.Count == 0 ? 1 : config.ScheduledTasks.Max(t => t.Id) + 1,
                Type = (ScheduledTaskType)cmb_type.SelectedItem,
                TargetKind = target.Kind,
                TargetServerId = target.ServerId,
                TargetClusterKey = target.ClusterKey,
                Mode = mode,
            };

            if (mode == ScheduleMode.DailyAtTime)
            {
                if (!TimeSpan.TryParse(txt_when.Text, out var t)) { MessageBox.Show("Enter a time as HH:mm."); return; }
                task.DailyTime = t;
            }
            else
            {
                if (!int.TryParse(txt_when.Text, out var h) || h < 1) { MessageBox.Show("Enter interval hours (>= 1)."); return; }
                task.IntervalHours = h;
            }

            config.ScheduledTasks.Add(task);
            RefreshTasks();
        }

        private void btn_removeTask_Click(object sender, RoutedEventArgs e)
        {
            int i = lst_tasks.SelectedIndex;
            if (i >= 0 && i < config.ScheduledTasks.Count) { config.ScheduledTasks.RemoveAt(i); RefreshTasks(); }
        }

        private void btn_browseBackup_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                txt_backupRoot.Text = dialog.SelectedPath;
        }

        private void btn_save_Click(object sender, RoutedEventArgs e)
        {
            if (ushort.TryParse(txt_stagger.Text, out var st)) config.AutoStartStaggerTime = st;
            if (!string.IsNullOrWhiteSpace(txt_backupRoot.Text)) config.BackupRoot = txt_backupRoot.Text.Trim();
            if (int.TryParse(txt_keep.Text, out var keep) && keep >= 1) config.BackupKeepCount = keep;
            config.CrashAutoRestartEnabled = chk_crashRestart.IsChecked == true;
            if (int.TryParse(txt_crashThreshold.Text, out var thr) && thr >= 1) config.CrashThresholdCount = thr;
            if (int.TryParse(txt_crashWindow.Text, out var win) && win >= 1) config.CrashWindowMinutes = win;
            config.Save();
            Close();
        }
    }
}
