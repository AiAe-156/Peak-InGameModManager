#nullable enable
using System;
using System.Net;
using System.Text.RegularExpressions;
namespace ModConfigEnhance.Overview;
internal static class ShopDocumentRules
{
    internal static string ImageUrl(string url)
    {
        url=WebUtility.HtmlDecode(url);
        if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase))
        {var parts=uri.AbsolutePath.Split('/');if(parts.Length>4&&parts[3]=="blob")return "https://raw.githubusercontent.com/"+parts[1]+"/"+parts[2]+"/"+string.Join("/",parts,4,parts.Length-4);}
        return url;
    }
    internal static string Prepare(string source)
    {
        source=source.Replace("\r","");
        // SVG badges are links rather than bitmap media. No executable HTML is rendered.
        source=Regex.Replace(source,@"!\[([^\]]*)\]\((https://img\.shields\.io/[^\s\)]+)\)",m=>"["+m.Groups[1].Value+"]("+m.Groups[2].Value+")",RegexOptions.IgnoreCase);
        source=Regex.Replace(source,"<img\\b[^>]*?src=[\"']([^\"']+)[\"'][^>]*>",m=>"\n\n![image]("+ImageUrl(m.Groups[1].Value)+")\n\n",RegexOptions.IgnoreCase);
        source=Regex.Replace(source,@"!\[([^\]]*)\]\((https://[^\s\)]+)\)",m=>"!["+m.Groups[1].Value+"]("+ImageUrl(m.Groups[2].Value)+")");
        source=Regex.Replace(source,@"</?p\b[^>]*>|</?dl\b[^>]*>","\n\n",RegexOptions.IgnoreCase);
        source=Regex.Replace(source,@"<dt\b[^>]*>(.*?)</dt>",m=>"\n**"+m.Groups[1].Value+"**: ",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        source=Regex.Replace(source,@"<dd\b[^>]*>(.*?)</dd>",m=>m.Groups[1].Value+"\n\n",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        return source;
    }
}
