using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a login profile backup file - SIMPLIFIED VERSION
    /// </summary>
    public class ProfileInfo : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _filePath = string.Empty;
        private DateTime _lastModified;
        private string _description = string.Empty;
        private long _fileSize;
        
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        
        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }
        
        public DateTime LastModified
        {
            get => _lastModified;
            set => SetProperty(ref _lastModified, value);
        }
        
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }
        
        /// <summary>
        /// Gets the file size in bytes
        /// </summary>
        public long FileSize
        {
            get => _fileSize;
            set
            {
                if (SetProperty(ref _fileSize, value))
                {
                    // Notify that FileSizeFormatted has also changed
                    OnPropertyChanged(nameof(FileSizeFormatted));
                }
            }
        }
        
        /// <summary>
        /// Gets a display-friendly file size
        /// </summary>
        public string FileSizeFormatted => FormatFileSize(FileSize);
        
        /// <summary>
        /// SIMPLIFIED: True if this is the login_w.bin system file
        /// </summary>
        public bool IsSystemFile => Path.GetFileName(FilePath).Equals("login_w.bin", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// SIMPLIFIED: True if this is the user's last selected active profile
        /// </summary>
        public bool IsLastUserChoice { get; set; }
        
        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}