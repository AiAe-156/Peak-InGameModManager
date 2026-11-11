#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace ModConfigEnhance.Overview;
internal sealed class ShopDownloadQueue
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _timeout;
    internal ShopDownloadQueue(int slots, TimeSpan timeout) { _slots = new SemaphoreSlim(slots); _timeout = timeout; }
    internal async Task<byte[]> Run(Func<CancellationToken, Task<byte[]>> read, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_timeout);
        await _slots.WaitAsync(timeout.Token).ConfigureAwait(false);
        try { return await read(timeout.Token).ConfigureAwait(false); }
        finally { _slots.Release(); }
    }
}
