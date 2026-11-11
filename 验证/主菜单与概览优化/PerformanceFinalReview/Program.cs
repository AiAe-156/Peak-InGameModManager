using System.Diagnostics;
using ModConfigEnhance.Overview;
using MCE.UpdateWorker;

// Independent final-review checks. Production sources are compiled read-only.
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }

// ── 1. StableRowCache: budget, LRU order, idempotent park, non-member remove ─────────────
foreach (int capacity in new[] { 0, 12, 24 })
{
    var cache = new StableRowCache<object>(capacity);
    var parked = new List<object>();
    for (int i = 0; i < 200; i++)
    {
        var tree = new object(); parked.Add(tree);
        var evicted = cache.Park(tree);
        Check(cache.Count <= capacity, $"idle budget never exceeded cap={capacity} i={i}");
        Check((evicted != null) == (i >= capacity), $"eviction starts exactly above budget cap={capacity} i={i}");
    }
    if (capacity > 0)
    {
        // Re-parking an already idle tree must not duplicate it or evict a different tree.
        var survivor = parked[^1];
        Check(cache.Park(survivor) == null, $"re-park of idle tree is idempotent cap={capacity}");
        Check(cache.Count == capacity, $"re-park keeps count cap={capacity}");
        // Remove of a non-member is a no-op (production calls Remove on wake for rows never parked).
        cache.Remove(new object());
        Check(cache.Count == capacity, $"remove of non-member is a no-op cap={capacity}");
        cache.Remove(survivor);
        Check(cache.Count == capacity - 1, $"remove of member frees a slot cap={capacity}");
        Check(cache.Park(survivor) == null, $"wake then park stays inside budget cap={capacity}");
    }
}
try { new StableRowCache<object>(-1); throw new Exception("negative capacity accepted"); }
catch (ArgumentOutOfRangeException) { Check(true, "negative capacity rejected"); }

// LRU order: after touching the oldest, the second-oldest must be evicted first.
{
    var cache = new StableRowCache<object>(3);
    var a = new object(); var b = new object(); var c = new object(); var d = new object();
    cache.Park(a); cache.Park(b); cache.Park(c);
    cache.Remove(a); cache.Park(a);              // a is now most recent
    Check(ReferenceEquals(cache.Park(d), b), "least recently used tree is evicted, not the re-parked one");
}

// ── 2. CoalescedProgress: gate, dedupe, stale-pending window, clear ─────────────────────
{
    var progress = new CoalescedProgress();
    progress.Publish("A");
    Check(progress.Take(0) == "A", "first publish is delivered");
    Check(progress.Take(0.01f) == null, "gate suppresses a second take inside 150ms");
    progress.Publish("B");
    progress.Publish("A");                        // equals _last, so pending stays "B"
    Check(progress.Take(1f) == "B", "pending value wins over a later value equal to the last shown one");
    Check(progress.Take(2f) == null, "no duplicate delivery of the same pending value");
    progress.Publish("A");
    Check(progress.Take(3f) == "A", "the skipped value is delivered on the next publish (self-healing)");
    progress.Publish("C"); progress.Clear();
    Check(progress.Take(4f) == null, "clear drops pending progress");
    progress.Publish("A");
    Check(progress.Take(5f) == "A", "clear resets the dedupe baseline so the same text can show again");
    // Background writers must never corrupt the coalescer.
    Parallel.For(0, 2000, i => progress.Publish("parallel " + (i % 7)));
    Check(progress.Take(6f)?.StartsWith("parallel ") == true, "concurrent background publishes stay readable");
}

// ── 3. ChangedFileSnapshot: failure retention, context switch, same-stamp blind spot ────
{
    string root = Path.Combine(AppContext.BaseDirectory, "sandbox_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string queue = Path.Combine(root, "queue.json"), transaction = Path.Combine(root, "transaction.json");
    File.WriteAllText(queue, "queue-0"); File.WriteAllText(transaction, "tx-0");
    string[] Files() => new[] { queue, transaction };
    string Read() => File.ReadAllText(queue) + "|" + File.ReadAllText(transaction);

    var snapshot = new ChangedFileSnapshot<string>();
    Check(snapshot.Refresh("ctx", Files, Read), "initial snapshot read");
    int reads = snapshot.Reads;
    for (int i = 0; i < 500; i++) snapshot.Refresh("ctx", Files, Read);
    Check(snapshot.Reads == reads, "500 stable polls never re-deserialize");

    string? last = snapshot.Value;
    File.WriteAllText(queue, "queue-malformed");   // a real change is what forces the read
    Check(!snapshot.Refresh("ctx", Files, () => throw new IOException("malformed")) && snapshot.Value == last && snapshot.Error == "malformed",
        "failed read keeps the last valid value and reports the error");
    Check(snapshot.Refresh("ctx", Files, Read), "recovers after a failed read");

    last = snapshot.Value;
    Check(!snapshot.Refresh("other", Files, () => throw new IOException("new env broken")) && snapshot.Value == null,
        "context change never retains the previous environment's value");
    Check(snapshot.Error == "new env broken", "context change failure is reported");
    Check(snapshot.Refresh("other", Files, Read), "new context recovers");

    // Racing multi-file read: the stamp is re-checked after reading.
    last = snapshot.Value;
    Check(!snapshot.Refresh("other", Files, () => { File.WriteAllText(transaction, "tx-race"); return "mixed"; }) && snapshot.Value == last,
        "racing multi-file read is rejected");

    // Same-length rewrite forced onto the same timestamp: the fingerprint cannot see it.
    File.WriteAllText(queue, "queue-1");
    var stamp = File.GetLastWriteTimeUtc(queue);
    Check(snapshot.Refresh("other", Files, Read), "content change with a new stamp is observed");
    string before = snapshot.Value!;
    File.WriteAllText(queue, "queue-2");                       // same length, different content
    File.SetLastWriteTimeUtc(queue, stamp);                     // same tick as the previous write
    bool observed = snapshot.Refresh("other", Files, Read);
    Check(!observed && snapshot.Value == before,
        "same-length rewrite inside one timestamp tick is invisible (documented residual risk)");
    File.SetLastWriteTimeUtc(queue, stamp.AddSeconds(1));
    Check(snapshot.Refresh("other", Files, Read) && snapshot.Value!.Contains("queue-2"), "a later tick reveals the rewrite");
    Directory.Delete(root, true);
}

// ── 4. StateChangePublication: dedupe, revision rollback, retry ─────────────────────────
{
    int writes = 0, revision = 0; string business = "idle";
    var publication = new StateChangePublication();
    bool Publish() => publication.Publish(() => revision + ":" + business, () => { writes++; revision++; });

    Check(Publish(), "first snapshot is written");
    for (int i = 0; i < 500; i++) Check(!Publish(), $"unchanged snapshot never rewrites i={i}");
    Check(writes == 1 && revision == 1, "unchanged snapshot keeps one write and one revision");

    business = "progress";
    Check(Publish() && revision == 2, "business change publishes a new revision");
    Check(!Publish(), "the just-written snapshot is deduplicated");

    // A failed write must not acknowledge the state and must stay retryable.
    business = "error";
    int revisionBeforeFailure = revision;
    bool threw = false;
    try { publication.Publish(() => revision + ":" + business, () => throw new IOException("sandbox failure")); }
    catch (IOException) { threw = true; }
    Check(threw, "write failure propagates to the caller");
    Check(revision == revisionBeforeFailure, "failed write leaves the business revision untouched");
    Check(Publish(), "failed publication is retried");
    Check(!Publish(), "retried publication then deduplicates");

    // Revision rollback inside the write callback (production QueueWorker.Save shape).
    int rollbackWrites = 0, rollbackRevision = 7;
    var rollback = new StateChangePublication();
    bool SaveShape() => rollback.Publish(() => "rev" + rollbackRevision, () =>
    {
        int previous = rollbackRevision;
        try { rollbackRevision++; if (rollbackWrites++ == 0) throw new IOException("disk full"); }
        catch { rollbackRevision = previous; throw; }
    });
    try { SaveShape(); } catch (IOException) { }
    Check(rollbackRevision == 7, "failed write rolls the revision back");
    Check(SaveShape() && rollbackRevision == 8, "retry after rollback commits exactly one revision");
    Check(!SaveShape(), "committed revision is not republished");
}

// ── 5. VirtualRowRange: buffer, clamping, oversized rows, controller walk ───────────────
{
    Check(VirtualRowRange.Contains(0, 36, 0, 380, 76), "first row is inside the buffered viewport");
    Check(!VirtualRowRange.Contains(1000, 36, 0, 380, 76), "far row is outside the buffered viewport");
    Check(VirtualRowRange.Contains(0, 36, -50, 380, 76), "negative scroll offset is clamped to zero");
    Check(VirtualRowRange.ScrollTo(0, 36, 400, 380) == 0, "row above the viewport scrolls up");
    Check(VirtualRowRange.ScrollTo(760, 36, 0, 380) == 416, "row below the viewport scrolls to its bottom edge");
    Check(VirtualRowRange.ScrollTo(100, 36, 100, 380) == 100, "visible row does not scroll");
    Check(VirtualRowRange.ScrollTo(0, 600, 0, 380) == 220, "row taller than the viewport aligns to its bottom edge (rows are 36-38px, unreachable in practice)");
    Check(VirtualRowRange.Next(0, -1, 10) == -1 && VirtualRowRange.Next(9, 1, 10) == -1, "controller walk stops at both ends");

    int index = 0, visited = 1; float scroll = 0;
    while (VirtualRowRange.Next(index, 1, 300) is int next && next >= 0)
    {
        index = next; visited++;
        scroll = VirtualRowRange.ScrollTo(index * 38, 36, scroll, 380);
        Check(VirtualRowRange.Contains(index * 38, 36, scroll, 380, 0), "controller target is on screen " + index);
    }
    Check(visited == 300 && index == 299, "controller reaches the last offscreen row");
}

// ── 6. RowIdentityLease: recycled identity cannot fire ──────────────────────────────────
{
    var lease = new RowIdentityLease(); string invoked = "";
    lease.Bind("A"); var stale = lease.Guard(() => invoked = "A");
    lease.Release(); lease.Bind("B");
    stale();
    Check(invoked == "", "listener from a released identity never runs");
    lease.Guard(() => invoked = "B")();
    Check(invoked == "B", "current identity listener runs");
    lease.Release(); lease.Guard(() => invoked = "wrong")();
    Check(invoked == "B", "unbound lease rejects every listener");
}

// ── 7. Pattern cost of RowUpdateStamp (production shape, measured here) ─────────────────
{
    // Production: _mods.Find(m=>"mod:"+m.Id==row.Key) per visible row per frame.
    var mods = Enumerable.Range(0, 300).Select(i => new { Id = "mod" + i, Latest = "1.0." + i }).ToList();
    var keys = Enumerable.Range(0, 16).Select(i => "mod:mod" + (i * 17)).ToArray();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    int found = 0;
    for (int frame = 0; frame < 600; frame++)          // 10 s at 60 fps
        foreach (string key in keys)
            if (mods.Find(m => "mod:" + m.Id == key) != null) found++;
    sw.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"MEASURE RowUpdateStamp pattern: {allocated / 1024.0:F1} KiB over 600 frames x 16 rows, {sw.Elapsed.TotalMilliseconds:F0} ms, hits={found}");
    Check(found == 600 * 16, "pattern lookup resolves every visible row");
    Check(allocated > 0, "pattern allocates per lookup (contradicts a zero-allocation claim)");

    // Same lookup with a precomputed dictionary: the fix shape.
    var byKey = mods.ToDictionary(m => "mod:" + m.Id, StringComparer.Ordinal);
    before = GC.GetAllocatedBytesForCurrentThread();
    sw.Restart();
    found = 0;
    for (int frame = 0; frame < 600; frame++)
        foreach (string key in keys)
            if (byKey.TryGetValue(key, out var hit) && hit != null) found++;
    sw.Stop();
    long fixedAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"MEASURE dictionary lookup: {fixedAllocated / 1024.0:F1} KiB, {sw.Elapsed.TotalMilliseconds:F0} ms, hits={found}");
    Check(fixedAllocated < allocated, "dictionary lookup allocates less than the linear scan pattern");
}

Console.WriteLine($"RESULT {passed} passed");
