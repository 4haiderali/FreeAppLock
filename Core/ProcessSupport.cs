using System.Runtime.InteropServices;

namespace FreeAppLock;

// Conservative compatibility check, not proof that a visible window has been secured.
// This release supports desktop windows owned directly by the target process, including
// packaged desktop apps. Host-only packaged windows aren't supported. Query failures
// are treated as unsupported rather than as protected.
internal readonly record struct ProcessSupport(bool CanEnforce, string Reason)
{
    public static ProcessSupport Supported => new(true, "Ordinary desktop process; window hiding is best-effort.");
    public static ProcessSupport Unsupported(string reason) => new(false, reason);
}

internal static class ProcessCompatibility
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    // Evaluate our own token once. The process integrity level cannot change without
    // launching another process; if it cannot be read, do not claim compatibility.
    private static readonly Lazy<int?> OwnIntegrity = new(() => ReadIntegrity(GetCurrentProcess()));

    internal static ProcessSupport Check(int pid, bool hasDirectWindow)
    {
        if (pid <= 0) return ProcessSupport.Unsupported("Invalid process ID.");
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero)
            return ProcessSupport.Unsupported("Windows denied access to inspect this process.");

        try
        {
            int packageLength = 0;
            int packageResult = GetPackageFullName(process, ref packageLength, IntPtr.Zero);
            bool packaged = packageResult == ErrorInsufficientBuffer || packageResult == 0;
            if (!packaged && packageResult != AppModelErrorNoPackage)
                return ProcessSupport.Unsupported("Unable to check the process package identity.");
            // Modern Notepad and other packaged desktop apps may own their HWND directly.
            // Package identity by itself does not make a window unenforceable.
            if (packaged && !hasDirectWindow)
                return ProcessSupport.Unsupported("This packaged app has no top-level window owned by its process (possibly hosted by ApplicationFrameHost).");

            int? ownLevel = OwnIntegrity.Value;
            int? targetLevel = ReadIntegrity(process);
            if (!ownLevel.HasValue || !targetLevel.HasValue)
                return ProcessSupport.Unsupported("Unable to verify the process security level.");
            if (targetLevel.Value > ownLevel.Value)
                return ProcessSupport.Unsupported("This app runs at a higher Windows integrity level (for example, as administrator).");

            return ProcessSupport.Supported;
        }
        catch (Exception)
        {
            return ProcessSupport.Unsupported("Unable to inspect the process securely.");
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static int? ReadIntegrity(IntPtr process)
    {
        if (!OpenProcessToken(process, TokenQuery, out IntPtr token)) return null;
        try
        {
            // First call gets the required TOKEN_MANDATORY_LABEL buffer size.
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int length);
            if (length < IntPtr.Size || length > 4096) return null;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _)) return null;
                // TOKEN_MANDATORY_LABEL begins with SID_AND_ATTRIBUTES; first field is SID*.
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero) return null;
                // SID: revision (1), subauthority count (1), identifier authority (6), RIDs.
                byte count = Marshal.ReadByte(sid, 1);
                if (count is < 1 or > 15) return null;
                return Marshal.ReadInt32(sid, 8 + (count - 1) * sizeof(uint));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref int packageFullNameLength, IntPtr packageFullName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);
}
