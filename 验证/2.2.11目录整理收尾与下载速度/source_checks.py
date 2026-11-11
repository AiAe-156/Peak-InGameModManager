# 2.2.11 源码断言：预览可取消且不计入待处理；下载速度字段贯通到队列行。
import io, os, sys
root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def read(p): return io.open(os.path.join(root, p), encoding='utf-8').read()
q = read('Updater/QueueWorker.cs'); p = read('Updater/WorkerParallelOperations.cs')
proj = read('src/Overview/ShopQueueProjection.cs'); inst = read('src/Overview/OverviewPage.Installation.cs')
view = read('src/Overview/UpdateQueueView.cs'); contracts = read('src/Updating/UpdateContracts.cs'); store = read('Updater/StoreClient.cs')
cancel_at = q.find('Operation == "cancel") { CancelLayout'); defer_at = q.find('等待当前更新完成后继续商店操作')
checks = [
    ('worker：layout cancel 入口', 'void CancelLayout(LayoutQueueRequest request)' in p and cancel_at > 0),
    ('worker：取消排在更新延后之前', 0 < cancel_at < defer_at),
    ('worker：只取消未确认的 layout-preview', 'task.Request.Operation!="layout-preview"' in p),
    ('worker：启动时关闭发起进程已退出的预览', 'CloseOrphanedPreview();' in q and 'Files.ProcessAlive(task.Request.SourceProcessId)' in p),
    ('worker：未知 layout 操作报错', 'Unsupported layout operation.' in q),
    ('投影：ready 预览为 Paused 而非 Preparing', 'IsReview(task)?UpdateStates.Paused' in proj),
    ('插件：返回即取消预览', 'cancelled: () => { OverviewUi.CloseModal(); CancelInstallationPreview(task); }' in inst),
    ('插件：有阻断也取消', '{ CancelInstallationPreview(task); OverviewUi.ShowText(' in inst),
    ('插件：超时不再直接放弃', 'Installation analysis timed out' not in inst),
    ('队列行：关闭预览按钮', '"preview-cancel"' in view),
    ('UpdateJob 有下载耗时与字节', 'DownloadSeconds' in contracts and 'DownloadTransferredBytes' in contracts),
    ('worker 记录耗时并扣除续传前缀', 'bytes - first' in q),
    ('下载开始先报起始偏移', 'Report the starting offset first' in store),
    ('队列行显示 速度 | 用时', 'row.Speed.text = DownloadNote(job);' in view and 'speed + " | " + time' in view),
]
for n, ok in checks: print(('PASS ' if ok else 'FAIL ') + n)
sys.exit(0 if all(ok for _, ok in checks) else 1)
