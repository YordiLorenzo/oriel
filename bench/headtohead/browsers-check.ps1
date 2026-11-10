# Check that no browser account was signed in: run it over SSH before browsers-start.ps1 and after the run. For each
# browser data folder it prints how many profiles Local State lists, how many of them have an account, how many
# accounts the profiles' Preferences files hold, and the first 16 hex digits of Local State's SHA-256 with its time.
# It prints counts only, never an account name. The bench folders (-Dir\browsers\chrome and \edge) must show 0
# accounts; the owner's own Chrome and Edge folders must print the same line before and after.
# -Roots checks other folders instead (name=path pairs), for example a copy of a profile known to be signed in.
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [string[]]$Roots = @("bench chrome=$Dir\browsers\chrome", "bench edge=$Dir\browsers\edge",
        "owner chrome=$env:LOCALAPPDATA\Google\Chrome\User Data", "owner edge=$env:LOCALAPPDATA\Microsoft\Edge\User Data")
)
$Roots = $Roots -split ','  # powershell -File passes "a=x,b=y" as one string
Add-Type -AssemblyName System.Web.Extensions
$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$json.MaxJsonLength = [int]::MaxValue
function Read-Json([string]$Path) {
    if (Test-Path -LiteralPath $Path) { $json.DeserializeObject([IO.File]::ReadAllText($Path)) }
}
foreach ($pair in $Roots) {
    $name, $root = $pair -split '=', 2
    if (-not (Test-Path -LiteralPath $root)) { "${name}: no folder"; continue }
    $state = Read-Json "$root\Local State"
    $cache = if ($state -and $state['profile']) { $state['profile']['info_cache'] }
    $profiles = 0; $signedIn = 0
    if ($cache) { foreach ($key in $cache.Keys) { $profiles++; if ($cache[$key]['user_name'] -or $cache[$key]['gaia_id']) { $signedIn++ } } }
    $prefs = 0; $accounts = 0
    foreach ($sub in Get-ChildItem -LiteralPath $root -Directory) {
        $p = Read-Json "$($sub.FullName)\Preferences"
        if ($p) { $prefs++; if ($p['account_info']) { $accounts += @($p['account_info']).Count } }
    }
    $file = Get-Item -LiteralPath "$root\Local State" -ErrorAction SilentlyContinue
    $stamp = if ($file) { (Get-FileHash -LiteralPath $file.FullName).Hash.Substring(0, 16) + ' ' + $file.LastWriteTime.ToString('s') } else { 'none' }
    "${name}: $profiles profiles, $signedIn with an account; $prefs Preferences files, $accounts accounts; Local State $stamp"
}
