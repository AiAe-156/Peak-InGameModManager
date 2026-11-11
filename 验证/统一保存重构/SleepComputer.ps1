$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EcoSleep {
    [DllImport("powrprof.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
'@
if (-not [EcoSleep]::SetSuspendState($false,$false,$false)) { throw ('Sleep failed: '+[Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
