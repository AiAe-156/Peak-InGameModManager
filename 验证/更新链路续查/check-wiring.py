from pathlib import Path
r=Path(__file__).resolve().parents[1].parent
v=(r/'src/Overview/UpdateQueueView.cs').read_text(encoding='utf-8-sig')
m=(r/'src/Overview/OverviewPage.Management.cs').read_text(encoding='utf-8-sig')
w=(r/'Updater/WorkerResourceBatch.cs').read_text(encoding='utf-8-sig')
checks=[('submitting projection','State = submitting ? "submitting" : "draft"' in v),('save disabled during acknowledgement','draftCount > 0 && !submitting' in v),('submitting has no remove action','job.State == "submitting") { actions.Clear();' in v),('draft details resolves command','id.StartsWith("draft:"' in v and 'ResourceOperationDraft.Snapshot().FirstOrDefault' in v),('batch details resolves full operations','SavedResourceReview.Details' in v),('queue shared impact','SavedResourceReview.ShowImpact' in v),('tools shared impact','SavedResourceReview.ShowImpact' in m),('queue binds batch identity','resourceBatchId: id.Substring("batch:".Length)' in v),('tools binds batch identity',m.count('resourceBatchId: batchId')==3),('worker checks batch identity','command.ResourceBatchId != batch.Id' in w),('mini shows submission status','Submitted; waiting for worker acknowledgement.' in (r/'src/Overview/OverviewPage.Updates.cs').read_text(encoding='utf-8-sig'))]
for name,ok in checks:
 assert ok,name
print(f'PASS {len(checks)} continuation wiring checks')
