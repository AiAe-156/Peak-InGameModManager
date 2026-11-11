"""2.2.12 源码断言：更新记录标题不再落到取任务，整理任务不走商店页，清除标记贯通。"""
from pathlib import Path
import re, sys
ROOT = Path(__file__).resolve().parents[2]
def src(p): return (ROOT / p).read_text(encoding='utf-8')
ok = fail = 0
def check(name, cond):
    global ok, fail
    print(('PASS ' if cond else 'FAIL ') + name); ok += cond; fail += (not cond)
view = src('src/Overview/UpdateQueueView.cs')
rebuild = view[view.index('private void Rebuild('):view.index('private static void RefreshRow(')]
check('Rebuild 标题行后 continue', re.search(r'_headers\.Add\(header\.rectTransform\);\s*y \+= 18;\s*continue;', rebuild) is not None)
check('Rebuild 不再强取 entry.Job!', 'entry.Job!' not in rebuild)
check('分组走 UpdateHistoryBatches', 'UpdateHistoryBatches.Group(' in view and 'class BatchGroup' not in view)
check('整理任务行有 查看/清除 动作', '"installation-review"' in view and '"installation-dismiss"' in view and '"installation-stop"' in view)
store = view[view.index('private void OpenStorePage('):view.index('private void OpenPackageFolder(')]
check('OpenStorePage 先拦无身份任务', store.index('IsInstallationTask(job)') < store.index('new OverviewRepository()'))
details = view[view.index('private void ShowDetails('):view.index('private void Network()')]
check('详情：整理任务先于商店分支', details.index('IsInstallationTask(job)') < details.index('if(job.StoreOperation){OpenStorePage'))
check('旧的 preview-cancel 已移除', '"preview-cancel"' not in view)
check('概览页传入 review 回调', 'StartPendingUpdates(), ShowInstallationReview);' in src('src/Overview/OverviewPage.Updates.cs'))
inst = src('src/Overview/OverviewPage.Installation.cs')
check('首次弹窗也走统一处理窗', 'ShowInstallationReview(task);' in inst and 'OverviewUi.ConfirmText(Loc.T("Confirm installation plan"' not in inst)
check('处理窗含 清除任务 并发 shop-dismiss', '"Clear task"' in inst and 'Send("shop-dismiss"' in inst)
check('投影跳过 Dismissed', '!task.Dismissed' in src('src/Overview/OverviewUpdateQueue.cs'))
check('契约 ShopTask.Dismissed', 'public bool Dismissed { get; set; }' in src('src/Updating/ShopContracts.cs'))
worker = src('Updater/QueueWorker.cs')
check('worker shop-dismiss 在延后判断之前', worker.index('"shop-dismiss"') < worker.index('throw new DeferredCommandException("资源准备进行中'))
par = src('Updater/WorkerParallelOperations.cs')
cf = par[par.index('void ClearFinished()'):par.index('void DismissShopTask(')]
check('ClearFinished 调用 DismissFinishedShopTask', 'DismissFinishedShopTask();' in cf)
check('DismissShopTask 拒绝 BlocksWrites', 'if(task.BlocksWrites)throw' in par)
print(f'=== {ok} passed, {fail} failed ===')
sys.exit(1 if fail else 0)
