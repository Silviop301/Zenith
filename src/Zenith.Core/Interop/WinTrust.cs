using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Zenith.Core.Bestiary;

namespace Zenith.Core.Interop;

/// <summary>
/// Authenticode check of a file, the same one Explorer shows under "Assinaturas digitais". Most Windows files
/// carry no signature of their own and are signed through a catalog, so that is tried when the file has none.
/// Revocation is checked only from the local cache: no network calls.
/// </summary>
internal static class WinTrust
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdChoiceCatalog = 2;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const uint WtdRevocationCheckNone = 0x10;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int CryptEFileError = unchecked((int)0x80092003);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustCatalogInfo
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        public string pcwszCatalogFilePath;
        public string pcwszMemberTag;
        public string pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfo;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string wszCatalogFile;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, in Guid action, ref WintrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provData, uint signerIndex, bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certIndex);

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr catAdmin, IntPtr subsystem, string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr catAdmin, SafeFileHandle file, ref uint hashSize, byte[]? hash, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr catAdmin, byte[] hash, uint hashSize, uint flags, IntPtr prevCatInfo);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [DllImport("crypt32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint CertGetNameStringW(IntPtr certContext, uint type, uint flags, IntPtr typePara, StringBuilder? name, uint size);

    private const uint CertNameSimpleDisplayType = 4;

    public static SignatureInfo Check(string path)
    {
        if (!File.Exists(path)) return SignatureInfo.NotChecked;
        try
        {
            var embedded = VerifyFile(path);
            if (embedded.Status != SignatureStatus.Unsigned) return embedded;
            return VerifyThroughCatalog(path) ?? embedded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SignatureInfo.NotChecked;
        }
    }

    private static SignatureInfo VerifyFile(string path)
    {
        var file = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            return Verify(WtdChoiceFile, filePtr);
        }
        finally
        {
            Marshal.DestroyStructure<WintrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    /// <summary>Looks the file's hash up in the Windows catalogs; null when no catalog lists it.</summary>
    private static SignatureInfo? VerifyThroughCatalog(string path)
    {
        foreach (var algorithm in new[] { "SHA256", "SHA1" })
        {
            if (!CryptCATAdminAcquireContext2(out var catAdmin, IntPtr.Zero, algorithm, IntPtr.Zero, 0)) continue;
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                uint size = 0;
                CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref size, null, 0);
                if (size == 0) continue;
                var hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref size, hash, 0)) continue;

                var catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, size, 0, IntPtr.Zero);
                if (catInfo == IntPtr.Zero) continue;
                try
                {
                    var info = new CatalogInfo { cbStruct = (uint)Marshal.SizeOf<CatalogInfo>() };
                    if (!CryptCATCatalogInfoFromContext(catInfo, ref info, 0)) continue;

                    var catalog = new WintrustCatalogInfo
                    {
                        cbStruct = (uint)Marshal.SizeOf<WintrustCatalogInfo>(),
                        pcwszCatalogFilePath = info.wszCatalogFile,
                        pcwszMemberTag = Convert.ToHexString(hash),
                        pcwszMemberFilePath = path,
                        hCatAdmin = catAdmin,
                    };
                    var hashPtr = Marshal.AllocHGlobal(hash.Length);
                    var catalogPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustCatalogInfo>());
                    try
                    {
                        Marshal.Copy(hash, 0, hashPtr, hash.Length);
                        catalog.pbCalculatedFileHash = hashPtr;
                        catalog.cbCalculatedFileHash = size;
                        Marshal.StructureToPtr(catalog, catalogPtr, false);
                        return Verify(WtdChoiceCatalog, catalogPtr);
                    }
                    finally
                    {
                        Marshal.DestroyStructure<WintrustCatalogInfo>(catalogPtr);
                        Marshal.FreeHGlobal(catalogPtr);
                        Marshal.FreeHGlobal(hashPtr);
                    }
                }
                finally
                {
                    CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
                }
            }
            finally
            {
                CryptCATAdminReleaseContext(catAdmin, 0);
            }
        }
        return null;
    }

    private static SignatureInfo Verify(uint unionChoice, IntPtr info)
    {
        var data = new WintrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
            dwUIChoice = WtdUiNone,
            fdwRevocationChecks = WtdRevokeNone,
            dwUnionChoice = unionChoice,
            pInfo = info,
            dwStateAction = WtdStateActionVerify,
            dwProvFlags = WtdCacheOnlyUrlRetrieval | WtdRevocationCheckNone,
        };
        var action = GenericVerifyV2;
        var result = WinVerifyTrust(IntPtr.Zero, action, ref data);
        try
        {
            return result switch
            {
                0 => new SignatureInfo(SignatureStatus.Valid, SignerName(data.hWVTStateData)),
                TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => new SignatureInfo(SignatureStatus.Unsigned),
                CryptEFileError => SignatureInfo.NotChecked,
                _ => new SignatureInfo(SignatureStatus.Invalid, SignerName(data.hWVTStateData)),
            };
        }
        finally
        {
            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(IntPtr.Zero, action, ref data);
        }
    }

    /// <summary>The leaf certificate's name, from the state WinVerifyTrust kept open.</summary>
    private static string? SignerName(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero) return null;
        var provData = WTHelperProvDataFromStateData(stateData);
        if (provData == IntPtr.Zero) return null;
        var signer = WTHelperGetProvSignerFromChain(provData, 0, false, 0);
        if (signer == IntPtr.Zero) return null;
        var providerCert = WTHelperGetProvCertFromChain(signer, 0);
        if (providerCert == IntPtr.Zero) return null;
        // CRYPT_PROVIDER_CERT: DWORD cbStruct, then the PCCERT_CONTEXT (pointer-aligned).
        var certContext = Marshal.ReadIntPtr(providerCert, IntPtr.Size);
        if (certContext == IntPtr.Zero) return null;

        var length = CertGetNameStringW(certContext, CertNameSimpleDisplayType, 0, IntPtr.Zero, null, 0);
        if (length <= 1) return null;
        var name = new StringBuilder((int)length);
        CertGetNameStringW(certContext, CertNameSimpleDisplayType, 0, IntPtr.Zero, name, length);
        return name.ToString();
    }
}
