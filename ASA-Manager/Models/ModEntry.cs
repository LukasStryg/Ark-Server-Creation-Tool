using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ARKServerCreationTool.Models
{
    /// <summary>A single mod in a server's ordered load-order list. Order = load order.</summary>
    public class ModEntry : INotifyPropertyChanged
    {
        private bool _enabled = true;

        public ulong ProjectId { get; set; }

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        [Newtonsoft.Json.JsonIgnore] private string? _displayName;

        /// <summary>Resolved display name (falls back to "#id"); populated from the metadata cache, not persisted.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? $"#{ProjectId}" : _displayName!;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public ModEntry() { }

        public ModEntry(ulong projectId, bool enabled = true)
        {
            ProjectId = projectId;
            _enabled = enabled;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
