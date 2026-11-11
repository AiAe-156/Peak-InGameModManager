#nullable enable
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace ModConfigEnhance.Overview;

/// <summary>Visible-row text only. Never clears/resizes shared fonts or changes their fallback settings.</summary>
internal sealed class HintPrewarm
{
    private readonly Queue<(TMP_FontAsset Font,string Text)> _queue=new();
    private readonly HashSet<(TMP_FontAsset Font,string Text)> _seen=new();
    private int _language=-1;
    internal void Clear() { _queue.Clear();_seen.Clear();_language=Loc.TranslationRevision; }
    internal void Enqueue(TMP_FontAsset? font,string text)
    {
        if(_language!=Loc.TranslationRevision)Clear();
        if(font==null || string.IsNullOrEmpty(text) || !_seen.Add((font,text)))return;
        // Bounded generations, including descriptions from long scrolling sessions.
        if(_seen.Count>512) { _seen.Clear();_queue.Clear();_seen.Add((font,text)); }
        _queue.Enqueue((font,text));
    }
    internal void Tick()
    {
        if(_queue.Count==0)return;
        if(_language!=Loc.TranslationRevision) { Clear();return; }
        var item=_queue.Dequeue();if(item.Font==null)return;
        // HasCharacters follows TMP's actual fallback chain and batches atlas application
        // through TMP. A single native glyph insertion is not preemptible: keep batches small.
        item.Font.HasCharacters(item.Text,out uint[] _,true,true);
    }
    internal static string Plain(string text)
    {
        var result=new System.Text.StringBuilder(text.Length);bool tag=false;
        foreach(char ch in text) { if(ch=='<') { tag=true;continue; }if(tag) { if(ch=='>')tag=false;continue; }result.Append(ch); }
        return result.ToString();
    }
    internal void EnqueueChunks(TMP_FontAsset? font,string text)
    {
        text=Plain(text);var unique=new HashSet<char>();var chunk=new System.Text.StringBuilder(24);
        foreach(char ch in text)
        {
            // Do not split UTF-16 pairs; non-BMP glyphs remain demand-loaded by TMP.
            if(char.IsSurrogate(ch) || char.IsControl(ch) || !unique.Add(ch))continue;
            chunk.Append(ch);
            if(chunk.Length<24)continue;
            Enqueue(font,chunk.ToString());chunk.Clear();
        }
        if(chunk.Length>0)Enqueue(font,chunk.ToString());
    }
}
