# 2.2.10 源码断言：worker 收尾文件已变的待重启记录；插件对同一批待重启记录每次启动只核对一次。
import io, os, sys
root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def read(p): return io.open(os.path.join(root, p), encoding='utf-8').read()
w = read('Updater/QueueWorker.cs'); q = read('src/Overview/OverviewUpdateQueue.cs'); m = read('src/Updating/UpdateMessageCatalog.cs')
checks = [
    ('worker：文件缺失或哈希变化置为 Superseded', 'Files.Hash(p.Target) != p.Hash))\n            { State(job, UpdateStates.Superseded' in w),
    ('worker：卸载记录不走该收尾', '!job.Uninstall && plugins.Any(p => !File.Exists(p.Target)' in w),
    ('worker：停用名装下按未加载判完成', 'PluginFileNames.OriginalPath(p.Target) != p.Target' in w),
    ('消息条目有三语', '"installed-files-changed-later"' in m),
    ('插件：同一批待重启记录不重复发送', 'observed == _observedPending) return;' in q),
    ('插件：仅当全为待重启时才跳过', 'pending.All(j => j.State == UpdateStates.PendingRestart)' in q),
    ('插件：发送失败清空记号', q.count('_observedPending = ""') + q.count('_observedPending=""') >= 2),
    ('10 秒节流保留', '_observeAt = DateTime.UtcNow.AddSeconds(10);' in q),
]
for n, ok in checks: print(('PASS ' if ok else 'FAIL ') + n)
sys.exit(0 if all(ok for _, ok in checks) else 1)
