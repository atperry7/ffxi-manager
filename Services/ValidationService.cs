using System;
using System.IO;
using System.Linq;
using FFXIManager.Configuration;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for validation operations
    /// </summary>
    public interface IValidationService
    {
        ValidationResult ValidateProfileName(string name);
        ValidationResult ValidateFilePath(string filePath);
        ValidationResult ValidateFileSize(long sizeBytes);
        ValidationResult ValidateDirectory(string directoryPath);
    }

    /// <summary>
    /// Result of a validation operation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; init; }
        public string? ErrorMessage { get; init; }

        public static ValidationResult Success() => new() { IsValid = true };
        public static ValidationResult Failure(string message) => new() { IsValid = false, ErrorMessage = message };
    }

    /// <summary>
    /// Service for validating user input and file operations
    /// </summary>
    public class ValidationService : IValidationService
    {
        private readonly IConfigurationService _configService;

        public ValidationService(IConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public ValidationResult ValidateProfileName(string name)
        {
            var config = _configService.ValidationConfig;

            if (string.IsNullOrWhiteSpace(name))
            {
                return ValidationResult.Failure(config.ValidationMessages["NameTooShort"]);
            }

            if (name.Length > config.MaxProfileNameLength)
            {
                return ValidationResult.Failure(
                    string.Format(config.ValidationMessages["NameTooLong"], config.MaxProfileNameLength));
            }

            if (name.Length < config.MinProfileNameLength)
            {
                return ValidationResult.Failure(config.ValidationMessages["NameTooShort"]);
            }

            // Check for invalid characters
            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(_configService.FileSystemConfig.InvalidFileNameChars.Select(s => s[0]))
                .Distinct();

            if (name.Any(c => invalidChars.Contains(c)))
            {
                return ValidationResult.Failure(config.ValidationMessages["InvalidCharacters"]);
            }

            // Check for reserved names
            if (config.ReservedProfileNames.Contains(name.ToUpperInvariant()))
            {
                return ValidationResult.Failure(
                    string.Format(config.ValidationMessages["ReservedName"], name));
            }

            return ValidationResult.Success();
        }

        public ValidationResult ValidateFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return ValidationResult.Failure("File path cannot be empty");
                }

                // Check if path is valid
                var fullPath = Path.GetFullPath(filePath);

                // Check if directory exists
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    return ValidationResult.Failure($"Directory does not exist: {directory}");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                return ValidationResult.Failure($"Invalid file path: {ex.Message}");
            }
        }

        public ValidationResult ValidateFileSize(long sizeBytes)
        {
            var config = _configService.ValidationConfig;

            if (sizeBytes < 0)
            {
                return ValidationResult.Failure("File size cannot be negative");
            }

            if (sizeBytes > config.MaxFileSizeBytes)
            {
                var maxSizeMB = config.MaxFileSizeBytes / (1024.0 * 1024.0);
                return ValidationResult.Failure(
                    string.Format(config.ValidationMessages["FileTooLarge"], $"{maxSizeMB:F1} MB"));
            }

            return ValidationResult.Success();
        }

        public ValidationResult ValidateDirectory(string directoryPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    return ValidationResult.Failure("Directory path cannot be empty");
                }

                if (!Directory.Exists(directoryPath))
                {
                    return ValidationResult.Failure($"Directory does not exist: {directoryPath}");
                }

                // Test write access
                var testFile = Path.Combine(directoryPath, $"test_{Guid.NewGuid()}.tmp");
                try
                {
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch
                {
                    return ValidationResult.Failure("Directory is not writable");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                return ValidationResult.Failure($"Invalid directory: {ex.Message}");
            }
        }
    }
}
