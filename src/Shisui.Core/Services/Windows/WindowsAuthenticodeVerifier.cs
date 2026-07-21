using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shisui.Core.Services.Windows;

/// <summary>Windows の Authenticode ポリシーでファイルの署名と発行元を検証する。</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsTrustedPublisher(string filePath, string expectedCommonName)
    {
        if (!File.Exists(filePath) || WinVerifyTrustFile(filePath) != 0)
        {
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057 // Authenticode 埋め込み証明書の取得には CreateFromSignedFile が必要
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false)
                .Equals(expectedCommonName, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static int WinVerifyTrustFile(string filePath)
    {
        var filePathPointer = Marshal.StringToCoTaskMemUni(filePath);
        var fileInfoPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPointer,
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

            var trustData = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfo = fileInfoPointer,
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref trustData);
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
            Marshal.FreeCoTaskMem(filePathPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);
}
