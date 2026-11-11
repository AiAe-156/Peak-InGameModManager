using System;
partial class ShellHarness {
    private void PumpShell()
    {
        try
        {
            if(_shell==null)return;
            // Advance cheap shell steps together; only yield after actual work used the budget.
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int steps=0;steps<8 && _shell!=null;steps++)
            {
                if(!_shell.MoveNext()) { (_shell as IDisposable)?.Dispose();_shell=null;break; }
                if((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency>=4)break;
            }
        }
        catch(Exception error) { FailOpen(error); }
    }
}
