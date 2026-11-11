using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCE.UpdateWorker;

internal static class PackSignature
{
    internal static void Verify(string root)
    {
        string releasePath = Path.Combine(root, "release.json");
        var release = File.Exists(releasePath) ? JsonNode.Parse(File.ReadAllText(releasePath)) : null;
        string path = Path.Combine(root, "signature.json");
        if (!File.Exists(path)) { if (release?["signed"]?.GetValue<bool>() == true) throw new IOException("作者资料缺少必需签名。"); return; }
        var data = JsonNode.Parse(File.ReadAllText(path))!;
        if (data["algorithm"]?.GetValue<string>() != "RSA-SHA256-PKCS1" || data["files"] is not JsonArray files) throw new IOException("作者资料签名格式无效。");
        byte[]? Key(string name) => data["publicKey"]?[name] is JsonNode value ? Convert.FromBase64String(value.GetValue<string>()) : null;
        using var rsa = RSA.Create(); rsa.ImportParameters(new RSAParameters { Exponent = Key("Exponent"), Modulus = Key("Modulus") });
        string serialized = files.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(serialized), Convert.FromBase64String(data["signature"]!.GetValue<string>()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new IOException("作者资料签名验证失败。");
        var seen = new HashSet<string>(Files.Compare);
        foreach (var item in files)
        {
            string relative = item!["path"]!.GetValue<string>(); string file = Files.Child(root, relative); Files.RejectLinks(root, file);
            if (!seen.Add(relative) || !File.Exists(file)) throw new IOException("签名文件重复或缺失：" + relative);
            using var stream = File.OpenRead(file);
            if (Convert.ToBase64String(FrameworkCompat.HashData(stream)) != item["sha256"]!.GetValue<string>()) throw new IOException("作者资料已改变：" + relative);
        }
        if (!Files.Inventory(root).Keys.Where(p => p != "signature.json").Select(p => p.Replace('\\', '/')).ToHashSet(Files.Compare).SetEquals(seen)) throw new IOException("作者资料含未签名文件。");
    }
}
