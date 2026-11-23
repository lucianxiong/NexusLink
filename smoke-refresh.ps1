$ErrorActionPreference='Stop'
$dir=(Resolve-Path ../smoke).Path
robocopy ../publish/bin "$dir/bin" /E /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { exit $LASTEXITCODE }
$app=Start-Process "$dir/NexusLink.exe" -WorkingDirectory $dir -PassThru
try {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
 $refresh=$null
 for($i=0;$i -lt 25;$i++){
  Start-Sleep -Seconds 1
  $app.Refresh()
  if ($app.HasExited) { throw "Application exited: $($app.ExitCode)" }
  $hwnd=$app.MainWindowHandle
  if ($hwnd -eq [IntPtr]::Zero) { continue }
  $window=[Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $cond=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'刷新节点')
  $refresh=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,$cond)
  if ($refresh) { break }
 }
 if (!$refresh) { throw 'Refresh button unavailable' }
 $refresh.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 for($i=0;$i -lt 75;$i++){
  Start-Sleep -Seconds 2
  $all=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
  $status=@($all | ForEach-Object {$_.Current.Name} | Where-Object { $_ -like '刷新完成*' -or $_ -like '刷新未完成*' } | Select-Object -First 1)
  if ($status.Count) { $status[0]; break }
 }
 if (!$status.Count) { throw 'Refresh timed out at smoke test deadline' }
} finally {
 if (!$app.HasExited) { Stop-Process -Id $app.Id }
 foreach ($name in 'mihomo','sing-box','xray') {
  Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\bin\*" } | Stop-Process
 }
}
Get-ChildItem "$dir/guiLogs" -File | ForEach-Object {
 Select-String -LiteralPath $_.FullName -Pattern 'KeeperService refresh FAILED: (links|poolsOk|no node|parse|harness)|KeeperService refreshed:' | Select-Object -Last 3 | ForEach-Object { $_.Line }
}
