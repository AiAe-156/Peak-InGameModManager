using System;
partial class Check {
 static int passed;static void Assert(bool ok,string n){if(!ok)throw new Exception(n);passed++;Console.WriteLine("PASS "+n);}
 static void Main(){
 _queueError="worker real error";_observeError="old observe failure";_error=_queueError;Complete(new Result{Executed=true});Assert(_error==_queueError&&_observeError=="","observe success retains actual queue error");
 _queueError="unreadable queue";Complete(new Result{Executed=true});Assert(_error=="unreadable queue","observe success retains read failure");
 _localError="explicit command failure";Complete(new Result{Executed=true,Error="observe failure"});Assert(_error==_localError&&_localError=="explicit command failure","explicit local error remains authoritative");
 _localError="";Complete(new Result{Executed=true,Error="observe failure"});Assert(_error=="observe failure","automatic failure remains visible");
 Complete(new Result{Executed=false});Assert(_error=="observe failure"&&_observeError=="observe failure","skipped task does not clear failure");
 _queueError="";Complete(new Result{Executed=true});Assert(_error==""&&_observeError=="","successful retry clears only automatic error");
 Console.WriteLine($"RESULT {passed} exact-production completion checks passed");
 }
}
