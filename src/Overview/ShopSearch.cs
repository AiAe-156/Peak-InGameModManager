#nullable enable
using System;
using System.Linq;
using System.Text;
namespace ModConfigEnhance.Overview;
internal static class ShopSearch
{
    internal static string Normalize(string value)
    {
        var result=new StringBuilder(value.Length);foreach(char c in value)if(char.IsLetterOrDigit(c))result.Append(char.ToUpperInvariant(c));return result.ToString();
    }
    internal static bool Matches(string key,string description,string query)
    {
        if(string.IsNullOrWhiteSpace(query))return true;
        string compact=Normalize(query),name=Normalize(key);
        if(compact.Length>0&&name.Contains(compact))return true;
        string all=Normalize(key+" "+description);
        return query.Split(new[]{' ','\t','_','-'},StringSplitOptions.RemoveEmptyEntries).All(word=>all.Contains(Normalize(word)));
    }
}
