using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels;

namespace FFXIManager.ViewModels
{
    public class DiscoverySettingsViewModel : Base.ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        public DiscoverySettingsViewModel() : this(ServiceLocator.SettingsService) {}

        public DiscoverySettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            LoadFromSettings();
            SaveCommand = new RelayCommand(() => Save());
            CancelCommand = new RelayCommand(() => { /* handled by dialog */ });
        }

        private string _includeNamesText = string.Empty;
        public string IncludeNamesText
        {
            get => _includeNamesText;
            set { _includeNamesText = value; OnPropertyChanged(); }
        }

        private string _excludeNamesText = string.Empty;
        public string ExcludeNamesText
        {
            get => _excludeNamesText;
            set { _excludeNamesText = value; OnPropertyChanged(); }
        }

        private string _ignoredPrefixesText = string.Empty;
        public string IgnoredPrefixesText
        {
            get => _ignoredPrefixesText;
            set { _ignoredPrefixesText = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private bool _enableDiagnostics;
        public bool EnableDiagnostics
        {
            get => _enableDiagnostics;
            set { _enableDiagnostics = value; OnPropertyChanged(); }
        }

        private bool _verboseLogging;
        public bool VerboseLogging
        {
            get => _verboseLogging;
            set { _verboseLogging = value; OnPropertyChanged(); }
        }

        private int _maxLogEntries;
        public int MaxLogEntries
        {
            get => _maxLogEntries;
            set { _maxLogEntries = value; OnPropertyChanged(); }
        }

        private static readonly char[] CsvSplitChars = new[] { ',', ';', '\n', '\r' };

        private static List<string> SplitCsv(string text)
        {
            return (text ?? string.Empty)
                .Split(CsvSplitChars, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string JoinCsv(IEnumerable<string> items)
        {
            return string.Join(", ", items ?? Array.Empty<string>());
        }

        private void LoadFromSettings()
        {
            var settings = _settingsService.LoadSettings();
            IncludeNamesText = JoinCsv(settings.ProcessDiscovery?.IncludeNames ?? new List<string>());
            ExcludeNamesText = JoinCsv(settings.ProcessDiscovery?.ExcludeNames ?? new List<string>());
            IgnoredPrefixesText = JoinCsv(settings.ProcessDiscovery?.IgnoredWindowTitlePrefixes ?? new List<string>());

            // Diagnostics
            EnableDiagnostics = settings.Diagnostics?.EnableDiagnostics ?? false;
            VerboseLogging = settings.Diagnostics?.VerboseLogging ?? false;
            MaxLogEntries = settings.Diagnostics?.MaxLogEntries ?? 1000;
        }

        public void Save()
        {
            var settings = _settingsService.LoadSettings();
            settings.ProcessDiscovery ??= new ProcessDiscoverySettings();
            settings.ProcessDiscovery.IncludeNames = SplitCsv(IncludeNamesText);
            settings.ProcessDiscovery.ExcludeNames = SplitCsv(ExcludeNamesText);
            settings.ProcessDiscovery.IgnoredWindowTitlePrefixes = SplitCsv(IgnoredPrefixesText);

            // Diagnostics
            settings.Diagnostics ??= new DiagnosticsOptions();
            settings.Diagnostics.EnableDiagnostics = EnableDiagnostics;
            settings.Diagnostics.VerboseLogging = VerboseLogging;
            settings.Diagnostics.MaxLogEntries = MaxLogEntries > 0 ? MaxLogEntries : 1000;

            _settingsService.SaveSettings(settings);
        }
    }
}
