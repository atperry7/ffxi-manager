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


        private void LoadFromSettings()
        {
            var settings = _settingsService.LoadSettings();

            // Diagnostics
            EnableDiagnostics = settings.Diagnostics?.EnableDiagnostics ?? false;
            VerboseLogging = settings.Diagnostics?.VerboseLogging ?? false;
            MaxLogEntries = settings.Diagnostics?.MaxLogEntries ?? 1000;
        }

        public void Save()
        {
            var settings = _settingsService.LoadSettings();

            // Diagnostics
            settings.Diagnostics ??= new DiagnosticsOptions();
            settings.Diagnostics.EnableDiagnostics = EnableDiagnostics;
            settings.Diagnostics.VerboseLogging = VerboseLogging;
            settings.Diagnostics.MaxLogEntries = MaxLogEntries > 0 ? MaxLogEntries : 1000;

            _settingsService.SaveSettings(settings);
        }
    }
}
