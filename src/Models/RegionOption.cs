using System.ComponentModel;

namespace DiscordTunnel.Models
{
    public sealed class RegionOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ShortName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Arguments { get; init; } = string.Empty;
        public string ProxyArguments { get; init; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
