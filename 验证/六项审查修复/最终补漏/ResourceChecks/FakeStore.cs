using System.Net;using System.IO.Compression;using System.Text.Json;
sealed class FakeStore:HttpMessageHandler
{
 public int Downloads;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  string url=request.RequestUri!.AbsoluteUri;
  if(url.Contains("/api/experimental/package/")){bool root=url.Contains("/Mod/");string name=root?"Mod":url.Contains("/Core/")?"Core":"Lib";var payload=JsonSerializer.Serialize(new{version_number="1.0.0",download_url="https://example.test/"+name+".zip",dependencies=root?new[]{"Team-Lib-1.0.0"}:Array.Empty<string>()});return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload)});}
  Downloads++;bool isMod=url.EndsWith("Mod.zip"),isCore=url.EndsWith("Core.zip");using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){void Add(string path,string value){using var writer=new StreamWriter(zip.CreateEntry(path).Open());writer.Write(value);}Add("manifest.json",JsonSerializer.Serialize(new{name=isMod?"Mod":isCore?"Core":"Lib",version_number="1.0.0",dependencies=isMod?new[]{"Team-Lib-1.0.0"}:Array.Empty<string>()}));Add(isMod?"mod.txt":isCore?"BepInEx/core/Test.dll":"patchers/Team-Lib/hook.txt","payload");}
  var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(ms.ToArray())};return Task.FromResult(response);
 }
}
