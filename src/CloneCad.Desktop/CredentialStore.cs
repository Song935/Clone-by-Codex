using System.Runtime.InteropServices;

namespace CloneCad.Desktop;

internal static class CredentialStore
{
    private const string Target = "CloneCad/Anthropic";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
    public static void Save(string key)
    {
        var pointer = Marshal.StringToCoTaskMemUni(key);
        try
        {
            var credential = new Credential
            {
                Type = 1,
                TargetName = Target,
                UserName = Environment.UserName,
                Comment = "CloneCad Anthropic API key",
                CredentialBlob = pointer,
                CredentialBlobSize = key.Length * 2,
                Persist = 2,
                TargetAlias = ""
            };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }
    public static string Read()
    {
        if (!CredRead(Target, 1, 0, out var pointer)) return "";
        try { var c = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2) ?? ""; }
        finally { CredFree(pointer); }
    }
}
