#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Overview;

internal static class IntroductionFormatting
{
    private const string Tokens = @"</?(?:b|i|u|s|color|size|link|font)(?:=[^>]*)?>|&(?:amp|lt|gt);|[\s\S]";
    internal static int ToLogical(string value,int raw)
    {
        int count=0;foreach(Match token in Regex.Matches(value,Tokens)){if(token.Value.StartsWith("<"))continue;if(token.Index>=raw)break;count++;}return count;
    }
    internal static int ToRaw(string value,int logical)
    {
        int count=0,last=0;foreach(Match token in Regex.Matches(value,Tokens)){if(token.Value.StartsWith("<"))continue;if(count++>=logical)return token.Index;last=token.Index+token.Length;}return last;
    }
    private sealed class Run {internal string Text="";internal int Start,End;internal List<string> Tags=new();}
    internal static string Apply(string value,int start,int end,string style,bool link)
    {
        int lo=Math.Min(start,end),hi=Math.Max(start,end);bool all=lo==hi;
        var runs=new List<Run>();var active=new List<string>();
        bool everySelectedHadStyle=true,hasSelected=false;
        foreach(Match token in Regex.Matches(value,Tokens))
        {
            if(token.Value.StartsWith("<"))
            {
                string tag=token.Value.Substring(1,token.Value.Length-2);if(tag.StartsWith("/")){string name=tag.Substring(1);int index=active.FindLastIndex(t=>Name(t)==name);if(index>=0)active.RemoveAt(index);}else active.Add(tag);continue;
            }
            var tags=new List<string>(active);if(all || token.Index>=lo&&token.Index<hi)
            {
                hasSelected=true;everySelectedHadStyle&=active.Contains(style);
                string name=Name(style);tags.RemoveAll(t=>Name(t)==name);tags.Add(style);
                if(link){tags.RemoveAll(t=>Name(t)=="color"||Name(t)=="u");tags.Add("color=#2689D8");tags.Add("u");}
            }
            runs.Add(new Run{Text=token.Value,Start=token.Index,End=token.Index+token.Length,Tags=tags});
        }
        bool removeStyle=hasSelected&&everySelectedHadStyle&&(style=="b"||style=="i"||style=="u"||style=="s");
        if(removeStyle)foreach(var run in runs)if(all||run.Start>=lo&&run.Start<hi)run.Tags.RemoveAll(t=>Name(t)==style);
        var output=new StringBuilder();var opened=new List<string>();
        foreach(var run in runs)
        {
            int common=0;while(common<opened.Count&&common<run.Tags.Count&&opened[common]==run.Tags[common])common++;
            for(int i=opened.Count-1;i>=common;i--)output.Append("</").Append(Name(opened[i])).Append('>');
            for(int i=common;i<run.Tags.Count;i++)output.Append('<').Append(run.Tags[i]).Append('>');
            output.Append(run.Text);opened=run.Tags;
        }
        for(int i=opened.Count-1;i>=0;i--)output.Append("</").Append(Name(opened[i])).Append('>');return output.ToString();
    }
    private static string Name(string tag){int equals=tag.IndexOf('=');return equals<0?tag:tag.Substring(0,equals);}
}
