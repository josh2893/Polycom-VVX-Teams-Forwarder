# PCP Phone Forwarder – 
Adds forwarding status fetch from **Teams callfw.php**, derived from the phone's Provisioning.Server in `runningConfig`.

### What it does
1) `GET /api/v1/mgmt/device/runningConfig` (phone)  
2) Parse `data.Provisioning.Server`  
3) Build Teams URL = `<origin>/callfw.php` (e.g., `https://ause.dm.sdg.teams.microsoft.com/callfw.php`)  
4) `GET` that URL and display the **first line** of the response (mirrors your PowerShell logic).

### Also includes
- HTTPS enforced; toggle to allow self-signed
- Forward, Check, Reboot mirroring your PS defaults
- Absolute URL support in `apiFetch(path, ...)` when `path` starts with `http`

### Run
```powershell
dotnet restore
dotnet run
```
