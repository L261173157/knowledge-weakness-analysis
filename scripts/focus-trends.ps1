Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$proc = Get-Process | Where-Object { $_.ProcessName -like '*KnowledgeWeakness*' } | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$trendsBtn = $allButtons.Item(7)
Write-Host ("Clicking [{0}] {1}" -f 7, $trendsBtn.Current.Name)
$invoke = $trendsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
[void]$invoke.Invoke()
Start-Sleep -Milliseconds 1500

# Now enumerate visible elements on the page
$root2 = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::IsContentElementProperty, $true)
$elts = $root2.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Host ("Content elements visible: {0}" -f $elts.Count)
foreach ($e in $elts) {
    $ct = $e.Current.LocalizedControlType
    $name = $e.Current.Name
    if ($name -or $ct) {
        Write-Host ("  [{0}] {1}" -f $ct, $name)
    }
}
