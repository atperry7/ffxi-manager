using System;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a login profile backup file
    /// </summary>
    public class ProfileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets the file size in bytes
        /// </summary>
        public long FileSize { get; set; }
        
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
    }
}