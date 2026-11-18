from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:100])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")
sw = "src/Overview/ShopWindow.cs"
# the "Tasks" button opens the shared queue (same data as the overview's update queue); a store task row opens its card
edit(sw, 'Button(_panel,T("Tasks","下载任务"),ShowTask,w-286,12,140);',
         'Button(_panel,T("Tasks","下载任务"),ShowQueue,w-286,12,140);')
edit(sw, "    private void ShowTask()\n    {\n",
"""    /// <summary>1.1.5 (user: "store tasks and update tasks must be one data source, same tasks, same history, in sync"):
    /// the store's task view is the update queue itself (embedded), fed by the same snapshot as the overview's queue.</summary>
    private UpdateQueueView? _queue;
    private void ShowQueue()
    {
        _showingTask=true;_showingQueue=true;ClearBody();_queue?.Dispose();
        var host=OverviewUi.Rect("ShopQueue",_body);OverviewUi.Fill(host);
        _queue=new UpdateQueueView(host,()=>{},()=>{},text=>_status.text=text,Network,id=>{ShowTask();});
        _queue.Open(OverviewUpdateQueue.Snapshot);
        _status.text=Loc.T("Same list as the update queue in the mod overview.","与模组概览中的更新队列是同一份列表。","與模組概覽中的更新佇列是同一份列表。");
    }
    private bool _showingQueue;
    private void ShowTask()
    {
        _showingQueue=false;
""")
edit(sw, "    private void ClearBody(){", "    private void ClearBody(){if(_queue!=null){_queue.Dispose();_queue=null;}")
s = (root / sw).read_text(encoding="utf-8")
# keep the embedded queue live
s = s.replace("    private void Update()\n    {\n        GamepadFrame();",
              "    private void Update()\n    {\n        GamepadFrame();\n        if(_showingQueue&&_queue!=null){OverviewUpdateQueue.Poll();_queue.Refresh(OverviewUpdateQueue.Snapshot,OverviewUpdateQueue.LastError);}", 1)
# leaving the task views clears both flags
s = s.replace("_showingTask=false;if(_selected!=null)_selected=null;", "_showingTask=false;_showingQueue=false;if(_selected!=null)_selected=null;", 1)
s = s.replace("if(_showingTask){_showingTask=false;_requestedTask=false;Draw();return true;}",
              "if(_showingTask&&!_showingQueue&&_queue==null&&_cameFromQueue){_cameFromQueue=false;ShowQueue();return true;}\n        if(_showingTask){_showingTask=false;_showingQueue=false;_requestedTask=false;Draw();return true;}", 1)
s = s.replace("_queue=new UpdateQueueView(host,()=>{},()=>{},text=>_status.text=text,Network,id=>{ShowTask();});",
              "_queue=new UpdateQueueView(host,()=>{},()=>{},text=>_status.text=text,Network,id=>{_cameFromQueue=true;ShowTask();});", 1)
s = s.replace("    private bool _showingQueue;", "    private bool _showingQueue,_cameFromQueue;", 1)
(root / sw).write_text(s, encoding="utf-8")
# the old "requested task" auto-jump must not pull the user out of the shared list
s = (root / sw).read_text(encoding="utf-8")
s = s.replace("if((_requestedTask||_showingTask)&&(task.State is", "if((_requestedTask||_showingTask&&!_showingQueue)&&(task.State is", 1)
(root / sw).write_text(s, encoding="utf-8")
# embedded queue: store rows open their card through the review callback
uq = "src/Overview/UpdateQueueView.cs"
edit(uq, "    internal UpdateQueueView(RectTransform host, Action all, Action selected, Action<string> status, Action network)\n        : this(host, host, all, selected, status)\n    { _embedded = true; _networkRequested = network; _width = _height = 0; Layout(); }",
         "    internal UpdateQueueView(RectTransform host, Action all, Action selected, Action<string> status, Action network, Action<string>? review = null)\n        : this(host, host, all, selected, status, review: review)\n    { _embedded = true; _networkRequested = network; _width = _height = 0; Layout(); }")
print("ok")
