# Start a WAA job script in the "waa" distro through WMI, outside the SSH session's job object, so it survives
# disconnects. Usage: waa-start.ps1 <script next to this one> <log path in the distro> [script arguments]
# The host/ scripts must sit together in one folder on a local drive of the PC.
param([string]$Script = 'waa-prepare.sh', [string]$Log = '/root/waa-prepare.log', [string]$Arguments = '')
$dir = '/mnt/' + $PSScriptRoot.Substring(0, 1).ToLower() + $PSScriptRoot.Substring(2).Replace('\', '/')
$cmd = "wsl.exe -d waa -u root -- bash -c ""bash '$dir/$Script' $Arguments > $Log 2>&1"""
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd }
"return $($r.ReturnValue) pid $($r.ProcessId)"
