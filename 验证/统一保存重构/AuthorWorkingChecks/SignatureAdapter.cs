namespace MCE.UpdateWorker;
// Filesystem/crypto adapters only; signature verification itself is linked production code.
internal static class FrameworkCompat{internal static byte[] HashData(Stream stream)=>System.Security.Cryptography.SHA256.HashData(stream);}
internal static class Files{
 internal static readonly StringComparer Compare=StringComparer.OrdinalIgnoreCase;
 internal static string Child(string root,string relative){if(Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p is ".." or "." or ""))throw new IOException("Unsafe path");return Path.Combine(root,relative);}
 internal static void RejectLinks(string root,string file){for(string? p=file;p!=null&&p.StartsWith(root,StringComparison.OrdinalIgnoreCase);p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Link rejected");}
 internal static Dictionary<string,string> Inventory(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(root,p),p=>"");
}
