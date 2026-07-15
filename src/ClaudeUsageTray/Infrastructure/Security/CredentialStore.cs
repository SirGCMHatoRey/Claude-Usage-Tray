using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Infrastructure.Security;

public interface ICredentialStore
{
    bool TryGetApiKey(out string? apiKey);
    void SaveApiKey(string apiKey);
    void DeleteApiKey();
    bool HasApiKey();

    bool TryGetSessionKey(out string? sessionKey);
    void SaveSessionKey(string sessionKey);
    void DeleteSessionKey();
    bool HasSessionKey();
    void SaveCfClearance(string value);
    bool TryGetCfClearance(out string? value);
    void SaveUserAgent(string ua);
    bool TryGetUserAgent(out string? ua);
}

/// <summary>
/// Stores the Admin API key in Windows Credential Manager via DPAPI-backed storage.
/// Never writes the key to disk in plaintext.
/// </summary>
public sealed class CredentialStore : ICredentialStore
{
    private const string TargetName = "ClaudeUsageTray_AdminApiKey";
    private const string SessionTargetName = "ClaudeUsageTray_ClaudeAiSession";
    private const string CfClearanceTargetName = "ClaudeUsageTray_CfClearance";
    private const string UserAgentTargetName = "ClaudeUsageTray_UserAgent";
    private readonly ILogger<CredentialStore> _logger;

    public CredentialStore(ILogger<CredentialStore> logger) => _logger = logger;

    public bool TryGetApiKey(out string? apiKey)
    {
        apiKey = null;
        try
        {
            var cred = ReadWindowsCredential(TargetName);
            if (cred is null) return false;
            apiKey = cred;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read credential from Windows Credential Manager");
            return false;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        WriteWindowsCredential(TargetName, apiKey);
        _logger.LogInformation("API key saved to Windows Credential Manager");
    }

    public void DeleteApiKey()
    {
        DeleteWindowsCredential(TargetName);
        _logger.LogInformation("API key deleted from Windows Credential Manager");
    }

    public bool HasApiKey()
    {
        TryGetApiKey(out var key);
        return !string.IsNullOrWhiteSpace(key);
    }

    public bool TryGetSessionKey(out string? sessionKey)
    {
        sessionKey = null;
        try
        {
            var cred = ReadWindowsCredential(SessionTargetName);
            if (cred is null) return false;
            sessionKey = cred;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Claude.ai session from credential manager");
            return false;
        }
    }

    // sessionKey here stores the FULL cookie header string (all cookies concatenated)
    public void SaveSessionKey(string cookieHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieHeader);
        WriteWindowsCredential(SessionTargetName, cookieHeader);
        _logger.LogInformation("Claude.ai session saved to Windows Credential Manager");
    }

    public void DeleteSessionKey()
    {
        DeleteWindowsCredential(SessionTargetName);
        DeleteWindowsCredential(CfClearanceTargetName);
        DeleteWindowsCredential(UserAgentTargetName);
    }

    public void SaveCfClearance(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            WriteWindowsCredential(CfClearanceTargetName, value);
    }

    public bool TryGetCfClearance(out string? value)
    {
        value = null;
        try { value = ReadWindowsCredential(CfClearanceTargetName); return value is not null; }
        catch { return false; }
    }

    public bool HasSessionKey()
    {
        TryGetSessionKey(out var key);
        return !string.IsNullOrWhiteSpace(key);
    }

    public void SaveUserAgent(string ua)
    {
        if (!string.IsNullOrWhiteSpace(ua))
            WriteWindowsCredential(UserAgentTargetName, ua);
    }

    public bool TryGetUserAgent(out string? ua)
    {
        ua = null;
        try { ua = ReadWindowsCredential(UserAgentTargetName); return ua is not null; }
        catch { return false; }
    }

    // ── Windows Credential Manager P/Invoke ──────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    private static string? ReadWindowsCredential(string target)
    {
        if (!CredRead(target, 1 /*CRED_TYPE_GENERIC*/, 0, out var ptr))
            return null;

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static void WriteWindowsCredential(string target, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = 1, // CRED_TYPE_GENERIC
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                UserName = Environment.UserName
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException($"CredWrite failed: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(blobPtr);
        }
    }

    private static void DeleteWindowsCredential(string target)
    {
        CredDelete(target, 1 /*CRED_TYPE_GENERIC*/, 0);
    }
}
