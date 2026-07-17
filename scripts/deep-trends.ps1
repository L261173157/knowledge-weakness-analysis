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
$invoke = $trendsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
[void]$invoke.Invoke()
Start-Sleep -Milliseconds 2500
$root2 = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
# Walk control elements (not just content)
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$elt = $walker.GetFirstChild($root2)
function Dump($e, $depth) {
    if (-not $e) { return }
    $ct = $e.Current.LocalizedControlType
    $name = $e.Current.Name
    $auto = $e.Current.AutomationId
    $cls = $e.Current.ClassName
    $bb = $e.Current.BoundingRectangle
    Write-Host (("  " * $depth) + ("<{0}> name='{1}' auto='{2}' class='{3}' {4}" -f $ct, $name, $auto, $cls, $bb))
    $c = $walker.GetFirstChild($e)
    while ($c) {
        Dump $c ($depth + 1)
        $c = $walker.GetNextSibling($c)
    }
}
Dump $root2 0
