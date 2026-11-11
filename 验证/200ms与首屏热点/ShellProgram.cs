using System;
using System.Collections;
using System.Threading;
partial class ShellHarness {
    IEnumerator? _shell;
    Exception? failure;
    int failures;
    void FailOpen(Exception error) { failure=error;failures++;(_shell as IDisposable)?.Dispose();_shell=null; }
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
    static void Main() {
        var h=new ShellHarness();h.PumpShell();Check(h.failure==null,"null shell is a no-op");
        var cheap=new Steps(20);h._shell=cheap;h.PumpShell();Check(cheap.calls>=1&&cheap.calls<=8,"cheap steps bounded to eight");
        while(h._shell!=null)h.PumpShell();Check(cheap.calls==21&&cheap.disposed==1,"complete shell drains and disposes once");
        var slow=new Steps(4,6);h._shell=slow;h.PumpShell();Check(slow.calls==1&&h._shell==slow,"step exceeding 4ms yields without draining");
        while(h._shell!=null)h.PumpShell();Check(slow.calls==5&&slow.disposed==1,"slow shell resumes and completes");
        var empty=new Steps(0);h._shell=empty;h.PumpShell();Check(h._shell==null&&empty.disposed==1,"empty iterator disposes immediately");
        var broken=new Steps(1,throws:true);h._shell=broken;h.PumpShell();Check(h.failure is InvalidOperationException&&h.failures==1&&h._shell==null&&broken.disposed==1,"failure follows existing cleanup path");
        Console.WriteLine($"RESULT {passed} exact-production PumpShell checks passed; no Unity visual assertions");
    }
    sealed class Steps : IEnumerator,IDisposable {
        readonly int count,delay;readonly bool throws;public int calls,disposed;
        public Steps(int count,int delay=0,bool throws=false) { this.count=count;this.delay=delay;this.throws=throws; }
        public object? Current=>null;
        public bool MoveNext() { calls++;if(throws)throw new InvalidOperationException("test");if(delay>0)Thread.Sleep(delay);return calls<=count; }
        public void Reset()=>throw new NotSupportedException();public void Dispose()=>disposed++;
    }
}
