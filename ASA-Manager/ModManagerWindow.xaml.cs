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
using ARKServerCreationTool.Services.CurseForge;

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
            RefreshModNames(forceApi: false);
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

        private void btn_copyToServers_Click(object sender, RoutedEventArgs e)
        {
            if (ASCTGlobalConfig.Instance.Servers.Count(s => s.ID != server.ID) == 0)
            {
                MessageBox.Show("There are no other servers to copy to.");
                return;
            }
            var mods = modItems.Select(m => new ModEntry(m.ProjectId, m.Enabled)).ToList();
            new CopyModsToServersWindow(server, mods) { Owner = this }.ShowDialog();
        }

        private async void RefreshModNames(bool forceApi)
        {
            ApplyCachedNames();
            var ids = modItems.Select(m => m.ProjectId).ToList();
            if (ids.Count == 0) { lbl_modStatus.Text = ""; return; }

            var client = AppServices.CurseForge();
            if (!client.HasKey)
            {
                lbl_modStatus.Text = "Add a CurseForge API key (Settings) to show mod names and check updates.";
                return;
            }

            var cache = AppServices.MetadataCache;
            var now = System.DateTimeOffset.UtcNow;
            var stale = ids.Where(id => forceApi || cache.IsStale(id, System.TimeSpan.FromHours(24), now)).ToList();
            if (stale.Count == 0) { lbl_modStatus.Text = ""; return; }

            lbl_modStatus.Text = "Resolving mod names from CurseForge…";
            try
            {
                await cache.RefreshAsync(stale, client, now);
                cache.Save();
                ApplyCachedNames();
                lbl_modStatus.Text = "";
            }
            catch (System.Exception ex) { lbl_modStatus.Text = $"CurseForge: {ex.Message}"; }
        }

        private void ApplyCachedNames()
        {
            foreach (var m in modItems)
                if (AppServices.MetadataCache.TryGet(m.ProjectId, out var meta) && !string.IsNullOrEmpty(meta.Name))
                    m.DisplayName = meta.Name!;
        }

        private void btn_refreshNames_Click(object sender, RoutedEventArgs e) => RefreshModNames(forceApi: true);

        private async void btn_checkUpdates_Click(object sender, RoutedEventArgs e)
        {
            var client = AppServices.CurseForge();
            if (!client.HasKey) { MessageBox.Show("Set a CurseForge API key in Settings to check for updates."); return; }

            var ids = modItems.Select(m => m.ProjectId).ToList();
            if (ids.Count == 0) return;

            if (server.RunningModVersions.Count == 0)
            {
                MessageBox.Show("No baseline yet. Start this server once so the tool records which mod versions are running; then update checks will be meaningful.");
                return;
            }

            lbl_modStatus.Text = "Checking CurseForge for updates…";
            try
            {
                var mods = await client.GetModsAsync(ids);
                var snapshot = server.RunningModVersions;
                var outdated = mods
                    .Where(mod => ModUpdateChecker.HasNewerFile(mod, snapshot.TryGetValue((ulong)mod.Id, out var v) ? v : (long?)null))
                    .Select(mod => mod.Name)
                    .ToList();
                lbl_modStatus.Text = "";
                MessageBox.Show(outdated.Count == 0
                    ? "All mods are up to date (relative to this server's last start)."
                    : "Updates available for:\n - " + string.Join("\n - ", outdated) +
                      "\n\nRestart the server (Server window → Restart to apply) to pull them.");
            }
            catch (System.Exception ex) { lbl_modStatus.Text = ""; MessageBox.Show($"Update check failed: {ex.Message}"); }
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
            if (from < 0) return;
            int insertBefore = GetDropInsertIndex(e);                       // 0..Count in the current list
            int to = from < insertBefore ? insertBefore - 1 : insertBefore; // removing `from` first shifts a later target down one
            if (to < 0) to = 0;
            if (to > modItems.Count - 1) to = modItems.Count - 1;
            ListReorder.Move(modItems, from, to);
        }

        private int GetDropInsertIndex(DragEventArgs e)
        {
            for (int i = 0; i < lst_mods.Items.Count; i++)
            {
                if (lst_mods.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem lbi)
                {
                    var bounds = new System.Windows.Rect(lbi.TranslatePoint(new System.Windows.Point(0, 0), lst_mods), lbi.RenderSize);
                    if (e.GetPosition(lst_mods).Y < bounds.Top + bounds.Height / 2) return i; // insert before item i
                }
            }
            return lst_mods.Items.Count; // below everything → insert at the end
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
