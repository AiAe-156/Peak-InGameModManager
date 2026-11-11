#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace ModConfigEnhance.Overview;
/// <summary>Read-only file-generation cache. A failed or racing read never replaces the last valid value.</summary>
internal sealed class ChangedFileSnapshot<T> where T:class
{
    private string _stamp="", _context="";
    internal T? Value {get;private set;}
    internal int Reads {get;private set;}
    internal string Error {get;private set;}="";
    internal bool Refresh(string context,Func<IEnumerable<string>> files,Func<T> read)
    {
        if(_context!=context) { _context=context;_stamp="";Value=null; }
        try
        {
            string Stamp()=>string.Join("\n",files().OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Select(p=>{
                var f=new FileInfo(p);return p+"|"+(f.Exists?f.Length+"|"+f.LastWriteTimeUtc.Ticks:"missing"); }));
            string before=Stamp();
            if(Value!=null && before==_stamp) { Error="";return false; }
            Reads++;var next=read();
            if(before!=Stamp())throw new IOException("Queue files changed while reading; retrying.");
            Value=next;_stamp=before;Error="";return true;
        }
        catch(Exception error) { Error=error.Message;return false; }
    }
}
