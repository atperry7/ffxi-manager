using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for OTP (One-Time Password) operations using TOTP algorithm
    /// Handles secure storage and generation of OTP codes for Square Enix authentication
    /// </summary>
    public partial class OTPService : IOTPService
    {
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly ILoggingService _loggingService;
        private const string OTP_CREDENTIAL_PREFIX = "FFXIManager.OTP";
        private const string OTP_USERNAME = "SquareEnixOTP";

        public OTPService(IWindowsCredentialsService credentialsService, ILoggingService loggingService)
        {
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<bool> StoreOTPSecretAsync(string profileFilePath, Guid accountId, string authenticationKey)
        {
            try
            {
                var normalizedKey = ValidateAndNormalizeAuthenticationKey(authenticationKey);
                if (normalizedKey == null)
                {
                    await _loggingService.LogWarningAsync("Invalid authentication key format provided for OTP storage");
                    return false;
                }

                var targetName = GenerateOTPCredentialTarget(profileFilePath, accountId);
                bool result = await _credentialsService.StorePasswordAsync(targetName, OTP_USERNAME, normalizedKey);

                if (result)
                {
                    await _loggingService.LogInfoAsync($"Successfully stored OTP secret for account {accountId}");
                }
                else
                {
                    await _loggingService.LogErrorAsync($"Failed to store OTP secret for account {accountId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error storing OTP secret", ex);
                return false;
            }
        }

        public async Task<string?> GenerateOTPCodeAsync(string profileFilePath, Guid accountId)
        {
            try
            {
                var targetName = GenerateOTPCredentialTarget(profileFilePath, accountId);
                var secret = await _credentialsService.RetrievePasswordAsync(targetName, OTP_USERNAME);

                if (string.IsNullOrEmpty(secret))
                {
                    await _loggingService.LogDebugAsync($"No OTP secret found for account {accountId}");
                    return null;
                }

                // Generate TOTP code
                var otpCode = GenerateTOTP(secret);
                await _loggingService.LogDebugAsync($"Generated OTP code for account {accountId}");

                return otpCode;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error generating OTP code", ex);
                return null;
            }
        }

        public async Task<bool> HasOTPSecretAsync(string profileFilePath, Guid accountId)
        {
            try
            {
                var targetName = GenerateOTPCredentialTarget(profileFilePath, accountId);
                return await _credentialsService.CredentialExistsAsync(targetName, OTP_USERNAME);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error checking OTP secret existence", ex);
                return false;
            }
        }

        public async Task<bool> DeleteOTPSecretAsync(string profileFilePath, Guid accountId)
        {
            try
            {
                var targetName = GenerateOTPCredentialTarget(profileFilePath, accountId);
                bool result = await _credentialsService.DeletePasswordAsync(targetName, OTP_USERNAME);

                if (result)
                {
                    await _loggingService.LogInfoAsync($"Successfully deleted OTP secret for account {accountId}");
                }
                else
                {
                    await _loggingService.LogWarningAsync($"Failed to delete OTP secret for account {accountId} (may not have existed)");
                }

                return result;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error deleting OTP secret", ex);
                return false;
            }
        }

        public string? ValidateAndNormalizeAuthenticationKey(string authenticationKey)
        {
            if (string.IsNullOrWhiteSpace(authenticationKey))
                return null;

            // Remove all spaces and convert to uppercase
            var normalized = authenticationKey.Replace(" ", "").ToUpperInvariant();

            // Square Enix authentication keys are typically 32 characters (Base32)
            // Example: OBQVO3CUGA4VA6SNPJGWQ33BI5DFEVKW
            if (normalized.Length != 32)
            {
                return null;
            }

            // Validate Base32 format (A-Z, 2-7)
            if (!Base32ValidationRegex().IsMatch(normalized))
            {
                return null;
            }

            return normalized;
        }

        public string GenerateOTPCredentialTarget(string profileFilePath, Guid accountId)
        {
            // Create a unique target name using profile path and account ID, prefixed for OTP
            var profileBytes = Encoding.UTF8.GetBytes(profileFilePath ?? "unknown");
            var profileHash = Convert.ToBase64String(profileBytes).Replace('/', '_').Replace('+', '-').Replace('=', 'X');

            // Truncate if too long to avoid Windows credential target name limits
            if (profileHash.Length > 50)
            {
                profileHash = profileHash.Substring(0, 50);
            }

            return $"{OTP_CREDENTIAL_PREFIX}.{profileHash}.{accountId:N}";
        }

        /// <summary>
        /// Generates a 6-digit TOTP code using the RFC 6238 algorithm
        /// </summary>
        /// <param name="secret">Base32-encoded secret key</param>
        /// <param name="timeStep">Time step in seconds (default 30 for most implementations)</param>
        /// <returns>6-digit TOTP code</returns>
        private string GenerateTOTP(string secret, int timeStep = 30)
        {
            var secretBytes = Base32Decode(secret);
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timeStepNumber = unixTime / timeStep;

            return GenerateHOTP(secretBytes, timeStepNumber);
        }

        /// <summary>
        /// Generates HOTP code using RFC 4226 algorithm
        /// </summary>
        private string GenerateHOTP(byte[] secret, long counter)
        {
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(counterBytes);

            var offset = hash[hash.Length - 1] & 0x0F;
            var truncatedHash = (hash[offset] & 0x7F) << 24 |
                               (hash[offset + 1] & 0xFF) << 16 |
                               (hash[offset + 2] & 0xFF) << 8 |
                               (hash[offset + 3] & 0xFF);

            var code = truncatedHash % 1000000;
            return code.ToString("D6");
        }

        /// <summary>
        /// Decodes a Base32 string to bytes
        /// </summary>
        private byte[] Base32Decode(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Input cannot be null or empty", nameof(input));

            input = input.ToUpperInvariant().Replace("=", "");

            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bits = new StringBuilder();

            foreach (char c in input)
            {
                int value = base32Chars.IndexOf(c);
                if (value < 0)
                    throw new ArgumentException($"Invalid Base32 character: {c}");

                bits.Append(Convert.ToString(value, 2).PadLeft(5, '0'));
            }

            var byteCount = bits.Length / 8;
            var bytes = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                var byteString = bits.ToString().Substring(i * 8, 8);
                bytes[i] = Convert.ToByte(byteString, 2);
            }

            return bytes;
        }

        /// <summary>
        /// Generated regex for Base32 validation pattern
        /// </summary>
        [GeneratedRegex(@"^[A-Z2-7]{32}$", RegexOptions.None)]
        private static partial Regex Base32ValidationRegex();
    }
}
