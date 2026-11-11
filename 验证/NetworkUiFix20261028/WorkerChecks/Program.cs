using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
string root=Path.Combine(AppContext.BaseDirectory,"isolated-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),"BepInEx");Directory.CreateDirectory(Path.Combine(root,"plugins"));
using var worker=new QueueWorker(root);
foreach(string mode in new[]{"custom","direct","system"}) {
 var command=new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Action="network",Network=new UpdateNetworkOptions{Mode=mode,ProxyAddress=mode=="custom"?"http://127.0.0.1:7897":""}};
 string p=Path.Combine(root,"MCE","commands",DateTime.UtcNow.Ticks+"_"+command.Id+".json");Files.Save(p,command);
 Console.WriteLine("posted="+mode+" currentBeforeProcess="+worker.Current.Network.Mode);
 worker.ProcessCommands();
 var disk=Files.Read<UpdateSnapshot>(Path.Combine(root,"MCE","queue.json"));
 if(disk.Network.Mode!=mode||worker.Current.Network.Mode!=mode||disk.Error.Length>0)throw new Exception("network command failed "+disk.Error);
 Console.WriteLine("PASS processed/persisted mode="+mode+" revision="+disk.Revision);
}
Console.WriteLine("PASS asynchronous command lifecycle; isolated worker, no live configuration/tasks touched");
