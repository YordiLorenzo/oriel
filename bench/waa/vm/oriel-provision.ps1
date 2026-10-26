# Provision the WAA benchmark VM: OpenSSH server with key login (the key made in the "waa" distro), and oriel installed
# for the signed-in user. Run once inside the VM (through WAA's /setup/execute); writes oriel-provision.log to the share.
$src = '\\host.lan\Data'
Start-Transcript "$src\oriel-provision.log"
Get-AuthenticodeSignature "$src\OpenSSH-Win64-v10.0.0.0.msi" | Format-List Status, SignerCertificate
# WaitForExit, not Start-Process -Wait: in Windows PowerShell -Wait also waits for every process the child starts.
(Start-Process msiexec -ArgumentList '/i', "$src\OpenSSH-Win64-v10.0.0.0.msi", '/quiet', '/norestart' -PassThru).WaitForExit()
Set-Service sshd -StartupType Automatic
Start-Service sshd
if (-not (Get-NetFirewallRule -Name 'oriel-bench-sshd' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name 'oriel-bench-sshd' -DisplayName 'SSH for the oriel benchmark' -Direction Inbound -Protocol TCP -LocalPort 22 -Action Allow | Out-Null
}
$keys = 'C:\ProgramData\ssh\administrators_authorized_keys'
Copy-Item "$src\waa_vm.pub" $keys -Force
icacls $keys /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F'
# From a local copy: started from the share, Windows holds the exe at an "Open File - Security Warning" prompt.
# No wait: "oriel install" leaves the daemon running, which would hold WAA's /setup/execute request open.
New-Item -ItemType Directory -Force C:\agent-tools | Out-Null
Copy-Item "$src\oriel.exe" C:\agent-tools\oriel.exe -Force
Start-Process C:\agent-tools\oriel.exe -ArgumentList 'install --autostart' -WindowStyle Hidden
Start-Sleep 6
Get-Service sshd | Format-List Name, Status
Stop-Transcript
