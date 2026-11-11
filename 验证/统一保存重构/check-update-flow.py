from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(p):return (r/p).read_text(encoding='utf-8-sig')
n=0
def check(ok,msg):
 global n
 assert ok,msg
 n+=1
q=read('src/Overview/OverviewUpdateQueue.cs');v=read('src/Overview/UpdateQueueView.cs');p=read('src/Overview/OverviewPage.Updates.cs');f=read('src/Overview/OverviewListView.Infrastructure.cs');t=read('src/Overview/OverviewTagFilter.cs')
check('Path.Combine(root, "worker-instance.lock")' in q,'launcher probes singleton not write lock')
check('WorkerAlreadyRunningException' in read('Updater/Program.cs'),'duplicate startup exits without worker error diagnostic')
check('Installation write lock is busy; retry later.' in read('src/Loc.UpdaterTerms.cs'),'write lock English key is translated in existing catalogue')
check('Concat(drafts)' in v and '"draft:" + command.Id' in v,'durable drafts visible in execution window')
check('save?.Invoke()' in v and 'SaveCurrentProfile(showNotice: true)' in p,'window saves through existing unified confirmation')
check('resource-batch.json' in q and 'State = "saved-resource"' in q,'accepted batch visible before individual jobs exist')
check('resource-retry' in v and 'resource-cancel' in v and 'SavedResourceReview.ShowImpact' in v and 'resource-ignore-dependencies' in read('src/Overview/SavedResourceReview.cs'),'batch controls reachable in queue')
check('Close(false); all();' in v,'queue closes before modal risk/feedback so dialogs not covered')
check('Save configuration to submit them to the worker.' in q,'staging does not claim worker enqueue')
check('SetFeedback(message' in p and 'ShowText' in p,'combined ordinary/component results retained and shown')
check('if (batch && !mod.HasUpdate) continue;' in q,'all-update summary excludes rows without updates')
check('Tags.include.All(p.MatchesStatus)' in f and '!Tags.exclude.Any(p.MatchesStatus)' in f,'component include/exclude use actual statuses')
check('Tags.include.Any(t => t.StartsWith("$"))' not in f,'no blanket component hiding')
check('componentCount?.Invoke(row.Tag)' in t,'filter counts include components')
check('bool hasUpdate = package.HasUpdate' in f and 'component.LatestVersion' in p,'badge filter target share component version policy')
check('Append(job.Error.Length > 0)' in v,'failed batch rebuilds controls')
print(f'PASS {n} update flow wiring checks')
