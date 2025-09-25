using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing credentials using the Windows Credential Manager
    /// </summary>
    public class WindowsCredentialsService : IWindowsCredentialsService
    {
        private readonly ILoggingService _loggingService;
        private const string CREDENTIAL_PREFIX = "FFXIManager";

        public WindowsCredentialsService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public Task<bool> StorePasswordAsync(string target, string username, string password)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(target))
                        throw new ArgumentException("Target cannot be null or empty", nameof(target));
                    if (string.IsNullOrWhiteSpace(username))
                        throw new ArgumentException("Username cannot be null or empty", nameof(username));
                    if (string.IsNullOrWhiteSpace(password))
                        throw new ArgumentException("Password cannot be null or empty", nameof(password));

                    var passwordBytes = Encoding.UTF8.GetBytes(password);
                    var credentialBlob = Marshal.AllocHGlobal(passwordBytes.Length);
                    Marshal.Copy(passwordBytes, 0, credentialBlob, passwordBytes.Length);

                    var credential = new CREDENTIAL
                    {
                        Type = CRED_TYPE.GENERIC,
                        TargetName = target,
                        UserName = username,
                        CredentialBlob = credentialBlob,
                        CredentialBlobSize = (uint)passwordBytes.Length,
                        Persist = CRED_PERSIST.LOCAL_MACHINE,
                        AttributeCount = 0,
                        Attributes = IntPtr.Zero,
                        Comment = "FFXIManager PlayOnline Member Account Password",
                        TargetAlias = string.Empty
                    };

                    try
                    {
                        bool result = CredWrite(ref credential, 0);
                        if (!result)
                        {
                            int error = Marshal.GetLastWin32Error();
                            await _loggingService.LogErrorAsync($"Failed to store credential for {target}: Win32 error {error}");
                            return false;
                        }

                        await _loggingService.LogInfoAsync($"Successfully stored credential for {target}");
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(credentialBlob);
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error storing Windows credential", ex);
                    return false;
                }
            });
        }

        public Task<string?> RetrievePasswordAsync(string target, string username)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(target))
                        throw new ArgumentException("Target cannot be null or empty", nameof(target));

                    bool result = CredRead(target, CRED_TYPE.GENERIC, 0, out IntPtr credentialPtr);
                    if (!result)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ERROR_NOT_FOUND)
                        {
                            await _loggingService.LogDebugAsync($"Credential not found for {target}");
                            return null;
                        }

                        await _loggingService.LogErrorAsync($"Failed to retrieve credential for {target}: Win32 error {error}");
                        return null;
                    }

                    try
                    {
                        var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

                        // Verify username matches if provided
                        if (!string.IsNullOrWhiteSpace(username) &&
                            !string.Equals(credential.UserName, username, StringComparison.OrdinalIgnoreCase))
                        {
                            await _loggingService.LogWarningAsync($"Username mismatch for credential {target}");
                            return null;
                        }

                        if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                        {
                            await _loggingService.LogWarningAsync($"Empty credential blob for {target}");
                            return null;
                        }

                        byte[] passwordBytes = new byte[credential.CredentialBlobSize];
                        Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, (int)credential.CredentialBlobSize);
                        string password = Encoding.UTF8.GetString(passwordBytes);

                        // DIAGNOSTIC: Log credential retrieval details (without exposing password)
                        await _loggingService.LogDebugAsync($"[DIAGNOSTIC] Credential retrieved for {target}: BlobSize={credential.CredentialBlobSize} bytes, Password Length={password.Length} characters");

                        // DIAGNOSTIC: Check if password contains null terminators or unexpected characters
                        if (password.Contains('\0'))
                        {
                            var nullIndex = password.IndexOf('\0');
                            await _loggingService.LogWarningAsync($"[DIAGNOSTIC] Password contains null terminator at position {nullIndex}, truncating");
                            password = password.Substring(0, nullIndex);
                        }

                        await _loggingService.LogDebugAsync($"Successfully retrieved credential for {target}");
                        return password;
                    }
                    finally
                    {
                        CredFree(credentialPtr);
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error retrieving Windows credential", ex);
                    return null;
                }
            });
        }

        public Task<bool> UpdatePasswordAsync(string target, string username, string password)
        {
            // Update is the same as store for Windows Credential Manager
            return StorePasswordAsync(target, username, password);
        }

        public Task<bool> DeletePasswordAsync(string target, string username)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(target))
                        throw new ArgumentException("Target cannot be null or empty", nameof(target));

                    bool result = CredDelete(target, CRED_TYPE.GENERIC, 0);
                    if (!result)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ERROR_NOT_FOUND)
                        {
                            await _loggingService.LogDebugAsync($"Credential not found for deletion: {target}");
                            return true; // Consider not found as success for deletion
                        }

                        await _loggingService.LogErrorAsync($"Failed to delete credential for {target}: Win32 error {error}");
                        return false;
                    }

                    await _loggingService.LogInfoAsync($"Successfully deleted credential for {target}");
                    return true;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error deleting Windows credential", ex);
                    return false;
                }
            });
        }

        public Task<bool> CredentialExistsAsync(string target, string username)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(target))
                        return false;

                    bool result = CredRead(target, CRED_TYPE.GENERIC, 0, out IntPtr credentialPtr);
                    if (result)
                    {
                        try
                        {
                            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

                            // If username is specified, verify it matches
                            if (!string.IsNullOrWhiteSpace(username))
                            {
                                return string.Equals(credential.UserName, username, StringComparison.OrdinalIgnoreCase);
                            }

                            return true;
                        }
                        finally
                        {
                            CredFree(credentialPtr);
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error checking Windows credential existence", ex);
                    return false;
                }
            });
        }

        public string GenerateCredentialTarget(string profileFilePath, Guid accountId)
        {
            // Create a unique target name using profile path and account ID
            // Use base64 encoding of the path to handle special characters
            var profileBytes = Encoding.UTF8.GetBytes(profileFilePath ?? "unknown");
            var profileHash = Convert.ToBase64String(profileBytes).Replace('/', '_').Replace('+', '-').Replace('=', 'X');

            // Truncate if too long to avoid Windows credential target name limits
            if (profileHash.Length > 50)
            {
                profileHash = profileHash.Substring(0, 50);
            }

            return $"{CREDENTIAL_PREFIX}.{profileHash}.{accountId:N}";
        }

        #region Win32 API Declarations

        private const int ERROR_NOT_FOUND = 1168;

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, CRED_TYPE type, int reservedFlag);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CredFree([In] IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public CRED_TYPE Type;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public CRED_PERSIST Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string UserName;
        }

        private enum CRED_TYPE : uint
        {
            GENERIC = 1,
            DOMAIN_PASSWORD = 2,
            DOMAIN_CERTIFICATE = 3,
            DOMAIN_VISIBLE_PASSWORD = 4,
            GENERIC_CERTIFICATE = 5,
            DOMAIN_EXTENDED = 6,
            MAXIMUM = 7,
            MAXIMUM_EX = (MAXIMUM + 1000)
        }

        private enum CRED_PERSIST : uint
        {
            SESSION = 1,
            LOCAL_MACHINE = 2,
            ENTERPRISE = 3
        }

        #endregion
    }
}