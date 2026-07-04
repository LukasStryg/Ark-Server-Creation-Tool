using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using ARKServerCreationTool.Services.Mods;

namespace ARKServerCreationTool
{
    /// <summary>Modal dialog for pasting/importing many mod ids or URLs at once.</summary>
    public partial class BulkAddModsWindow : Window
    {
        public IReadOnlyList<ulong> ParsedIds { get; private set; } = new List<ulong>();

        public BulkAddModsWindow() => InitializeComponent();

        private void btn_paste_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
                txt_bulk.Text += (txt_bulk.Text.Length > 0 ? "\n" : "") + Clipboard.GetText();
        }

        private void btn_import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                try { txt_bulk.Text += (txt_bulk.Text.Length > 0 ? "\n" : "") + File.ReadAllText(dlg.FileName); }
                catch (Exception ex) { MessageBox.Show($"Could not read file: {ex.Message}"); }
            }
        }

        private void btn_ok_Click(object sender, RoutedEventArgs e)
        {
            ParsedIds = ModIdParser.ParseMany(txt_bulk.Text);
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
