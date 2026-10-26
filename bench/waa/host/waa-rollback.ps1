# Remove the October 2026 cumulative update (KB5127648, build 26200.9711), which turned off nested
# virtualization on AMD CPUs (microsoft/WSL#41917), and pause Windows Update for 7 days so it isn't
# reinstalled before Microsoft ships the fix. A reboot afterwards finishes the removal.
$pkg = 'Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9711.1.30'
dism /online /remove-package /packagename:$pkg /norestart /quiet
"dism exit code: $LASTEXITCODE (3010 = removed, reboot needed)"
$now = (Get-Date).ToUniversalTime()
$start = $now.ToString('yyyy-MM-ddTHH:mm:ssZ')
$end = $now.AddDays(7).ToString('yyyy-MM-ddTHH:mm:ssZ')
$k = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
foreach ($n in 'PauseUpdatesStartTime', 'PauseFeatureUpdatesStartTime', 'PauseQualityUpdatesStartTime') { Set-ItemProperty $k $n $start }
foreach ($n in 'PauseUpdatesExpiryTime', 'PauseFeatureUpdatesEndTime', 'PauseQualityUpdatesEndTime') { Set-ItemProperty $k $n $end }
"Windows Update paused until $end"
