#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed class ShopIcon : MonoBehaviour
{
    private static readonly ShopLeaseCache<Texture2D> Textures = new(128, 32 * 1024 * 1024, texture => UnityEngine.Object.Destroy(texture));
    private ShopLeaseCache<Texture2D>.Entry? _lease;
    private int _generation;
    internal void Load(string url)
    {
        Clear();
        GetComponent<RawImage>().color = ModConfigLook.Surface(new Color(.3f,.35f,.36f,1));
        if (!ShopUrlPolicy.TryNormalize(url, out url)) return;
        var cached = Textures.Acquire(url);
        if (cached != null) { Set(cached); return; }
        StartCoroutine(Read(url, _generation));
    }
    private void Set(ShopLeaseCache<Texture2D>.Entry entry)
    {
        _lease = entry;
        var image = GetComponent<RawImage>(); image.texture = entry.Value; image.color = Color.white;
    }
    private void Clear()
    {
        _generation++; StopAllCoroutines();
        var image = GetComponent<RawImage>(); if (image != null) image.texture = null;
        Textures.Release(_lease); _lease = null;
    }
    private void OnDestroy() => Clear();
    private IEnumerator Read(string url, int generation)
    {
        var task = ShopResourceCache.Get(url);
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted) { _ = task.Exception; yield break; }
        if (task.IsCanceled || generation != _generation) yield break;
        var cached = Textures.Acquire(url);
        if (cached != null) { Set(cached); yield break; }
        // Header dimensions are checked before Unity allocates the decoded pixel buffer.
        if (!ShopImageBounds.Allows(task.Result, 2048)) yield break;
        var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            if (!texture.LoadImage(task.Result)) { Destroy(texture); yield break; }
        }
        catch { Destroy(texture); yield break; }
        UnityEngine.Object.DontDestroyOnLoad(texture);
        var entry = Textures.Add(url, texture, (long)texture.width * texture.height * 4);
        if (entry != null) Set(entry);
    }
}
