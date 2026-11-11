using ModConfigEnhance.Diagnostics;
using System.IO.Compression;
var index = new LogRepeatIndex();
index.Update(new[] { new MceLogRecord { Epoch="e", Sequence=1, Level=MceLogLevel.Info, Message="first" } });
index.Update(Array.Empty<MceLogRecord>());
Console.WriteLine("empty_snapshot_stale_groups=" + index.Groups.Count);
index.Clear();
index.Update(new[] { new MceLogRecord { Epoch="e", Sequence=1, Level=MceLogLevel.Error, Message="same" }, new MceLogRecord { Epoch="e", Sequence=2, Level=MceLogLevel.Error, Message="same" } });
index.Update(new[] { new MceLogRecord { Epoch="e", Sequence=1, Level=MceLogLevel.Error, Message="same", StackTrace="changed stack" }, new MceLogRecord { Epoch="e", Sequence=2, Level=MceLogLevel.Error, Message="same" } });
Console.WriteLine("changed_first_record_stale_first=" + index.Groups.Single(g=>g.Key.EndsWith("same\u001f")).First.Sequence);
string root=Path.Combine(AppContext.BaseDirectory,"fixture");Directory.CreateDirectory(root);string path=Path.Combine(root,"unicode.log");
File.WriteAllBytes(path,new byte[]{(byte)'a',(byte)'\n',0xE4});
using(var reader=new LogFileReader(path)) { reader.Read(); using(var file=new FileStream(path,FileMode.Append))file.Write(new byte[]{0xB8,0xAD,(byte)'\n'}); Console.WriteLine("utf8_split="+reader.Read().Last().Message); }
