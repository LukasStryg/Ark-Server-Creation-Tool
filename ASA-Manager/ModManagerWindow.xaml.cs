using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ARKServerCreationTool.Models;
using ARKServerCreationTool.Services.Common;

namespace ARKServerCreationTool
{
    /// <summary>Per-server mod manager: reorderable load-order list with enable toggles, add and remove.</summary>
    public partial class ModManagerWindow : Window
    {
        private readonly ASCTServerConfig server;
        private readonly ObservableCollection<ModEntry> modItems;

        // In-process buffer for copy/paste between servers (persists across window instances).
        private static List<ModEntry> copyBuffer = new();

        public ModManagerWindow(ASCTServerConfig server)
        {
            InitializeComponent();
            this.server = server;

            modItems = new ObservableCollection<ModEntry>(server.Mods.Select(m => new ModEntry(m.ProjectId, m.Enabled)));
            foreach (var m in modItems) m.PropertyChanged += Mod_PropertyChanged;
            modItems.CollectionChanged += (_, __) => UpdatePreview();
            lst_mods.ItemsSource = modItems;

            lbl_header.Text = $"Mods for {server.Name}";
            UpdatePreview();
        }

        private void Mod_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            var ids = modItems.Where(m => m.Enabled).Select(m => m.ProjectId).ToList();
            txt_preview.Text = ids.Count > 0 ? $"-mods={string.Join(",", ids)}" : "(no enabled mods)";
        }

        private void btn_add_Click(object sender, RoutedEventArgs e)
        {
            if (ulong.TryParse(txt_addId.Text.Trim(), out ulong id))
            {
                if (!modItems.Any(m => m.ProjectId == id))
                {
                    var entry = new ModEntry(id);
                    entry.PropertyChanged += Mod_PropertyChanged;
                    modItems.Add(entry);
                }
                txt_addId.Clear();
            }
            else MessageBox.Show("Enter a numeric CurseForge Project ID.");
        }

        private void AddEntries(IEnumerable<ModEntry> entries)
        {
            int added = 0;
            foreach (var src in entries)
            {
                if (modItems.Any(m => m.ProjectId == src.ProjectId)) continue;
                var entry = new ModEntry(src.ProjectId, src.Enabled);
                entry.PropertyChanged += Mod_PropertyChanged;
                modItems.Add(entry);
                added++;
            }
            MessageBox.Show($"Added {added} mod(s).");
        }

        private void btn_bulkAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new BulkAddModsWindow { Owner = this };
            if (dlg.ShowDialog() == true)
                AddEntries(dlg.ParsedIds.Select(id => new ModEntry(id)));
        }

        private void btn_copyMods_Click(object sender, RoutedEventArgs e)
        {
            var source = lst_mods.SelectedItems.Count > 0
                ? lst_mods.SelectedItems.Cast<ModEntry>()
                : (IEnumerable<ModEntry>)modItems;
            copyBuffer = source.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
            MessageBox.Show($"Copied {copyBuffer.Count} mod(s) to the buffer.");
        }

        private void btn_pasteMods_Click(object sender, RoutedEventArgs e)
        {
            AddEntries(copyBuffer);
        }

        private void btn_up_Click(object sender, RoutedEventArgs e)
        {
            int i = lst_mods.SelectedIndex;
            ListReorder.MoveUp(modItems, i);
            if (i > 0) lst_mods.SelectedIndex = i - 1;
        }

        private void btn_down_Click(object sender, RoutedEventArgs e)
        {
            int i = lst_mods.SelectedIndex;
            ListReorder.MoveDown(modItems, i);
            if (i >= 0 && i < modItems.Count - 1) lst_mods.SelectedIndex = i + 1;
        }

        private void btn_remove_Click(object sender, RoutedEventArgs e)
        {
            var selected = lst_mods.SelectedItems.Cast<ModEntry>().ToList();
            if (selected.Count == 0) return;

            var wipe = MessageBox.Show(
                "Also delete the cached mod files under ShooterGame\\...\\Mods\\83374\\<id> for the removed mods?\n" +
                "(Fixes stale-cache crashes; files re-download on next start.)",
                "Remove mods", MessageBoxButton.YesNoCancel);
            if (wipe == MessageBoxResult.Cancel) return;

            foreach (var m in selected)
            {
                modItems.Remove(m);
                if (wipe == MessageBoxResult.Yes) TryWipeModCache(m.ProjectId);
            }
        }

        private void TryWipeModCache(ulong projectId)
        {
            try
            {
                string dir = Path.Combine(server.GameDirectory,
                    @"ShooterGame\Binaries\Win64\ShooterGame\Mods\83374", projectId.ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) { MessageBox.Show($"Could not delete mod cache for {projectId}: {ex.Message}"); }
        }

        // ---- Drag-and-drop reorder ----
        private System.Windows.Point dragStart;

        private void lst_mods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => dragStart = e.GetPosition(null);

        private void lst_mods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var diff = dragStart - e.GetPosition(null);
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (lst_mods.SelectedItem is ModEntry item)
                DragDrop.DoDragDrop(lst_mods, item, DragDropEffects.Move);
        }

        private void lst_mods_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ModEntry)) is not ModEntry dragged) return;
            int from = modItems.IndexOf(dragged);
            int to = GetDropIndex(e);
            ListReorder.Move(modItems, from, to);
        }

        private int GetDropIndex(DragEventArgs e)
        {
            for (int i = 0; i < lst_mods.Items.Count; i++)
            {
                if (lst_mods.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem lbi)
                {
                    var bounds = new System.Windows.Rect(lbi.TranslatePoint(new System.Windows.Point(0, 0), lst_mods), lbi.RenderSize);
                    if (e.GetPosition(lst_mods).Y < bounds.Top + bounds.Height / 2) return i;
                }
            }
            return modItems.Count - 1;
        }

        private void btn_done_Click(object sender, RoutedEventArgs e)
        {
            server.Mods = modItems.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
            DialogResult = true;
            Close();
        }

        private void btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
