using MCE.UpdateWorker;
internal static class Extra
{
    internal static void Run()
    {
        string root = System.IO.Path.Combine(AppContext.BaseDirectory,"adapter-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string target=System.IO.Path.Combine(root,"journal.json"), source=System.IO.Path.Combine(root,"new.json");
        Files.Save(target,new { Name="old", Protocol=2 }); Files.Save(target,new { Name="新状态", Protocol=2 });
        if (Files.Read<System.Text.Json.Nodes.JsonObject>(target)["Name"]!.GetValue<string>() != "新状态") throw new Exception("JSON/atomic save");
        File.WriteAllText(source,"replacement"); FrameworkCompat.MoveOverwrite(source,target);
        if (File.ReadAllText(target)!="replacement" || File.Exists(source)) throw new Exception("atomic replacement");
        if (FrameworkCompat.Hex(FrameworkCompat.HashData(System.Text.Encoding.UTF8.GetBytes("abc"))) != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD") throw new Exception("SHA256");
        foreach(string invalid in new[]{"../escape","C:/absolute","a/../b","a./x","a /x"}) { bool rejected=false;try{Files.Child(root,invalid);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("unsafe child"); }
#if NETFRAMEWORK
        if (FrameworkPath.GetRelativePath(root, System.IO.Path.Combine(root,"百分%20#","x")) != @"百分%20#\x") throw new Exception("relative path encoding");
        if (FrameworkPath.IsPathFullyQualified(@"C:relative") || !FrameworkPath.IsPathFullyQualified(@"C:\absolute")) throw new Exception("fully qualified");
#endif
        using var rsa=System.Security.Cryptography.RSA.Create(); byte[] payload=System.Text.Encoding.UTF8.GetBytes("protocol2");
        var signed=rsa.SignData(payload,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        if(!rsa.VerifyData(payload,signed,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1))throw new Exception("RSA SHA256");
        Console.WriteLine("PASS adapters: JSON atomic save/replace, SHA256, traversal guards, Windows path semantics, RSA-SHA256-PKCS1");
    }
}
