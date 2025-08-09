using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a login profile backup file
    /// </summary>
    public class ProfileInfo : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _filePath = string.Empty;
        private DateTime _lastModified;
        private bool _isActive;
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
        
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
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