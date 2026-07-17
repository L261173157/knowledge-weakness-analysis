Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$proc = Get-Process | Where-Object { $_.ProcessName -like '*KnowledgeWeakness*' } | Select-Object -First 1
if (-not $proc) { throw 'App not running' }
$hwnd = $proc.MainWindowHandle

function Bring-To-Front {
    [void][Win32]::ShowWindow($hwnd, 9)
    [void][Win32]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
    [void][Win32]::BringWindowToTop($hwnd)
    [void][Win32]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 250
}

function Snap {
    param([string]$Out)
    $rect = New-Object Win32+RECT
    [void][Win32]::GetWindowRect($hwnd, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][Win32]::PrintWindow($hwnd, $hdc, 0x00000002)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("  saved {0}" -f $Out)
}

# The first 9 buttons in the window's descendant tree, in source order, are the
# sidebar navigation buttons (sidebar lives before the page content in MainWindow).
Bring-To-Front
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
Write-Host ("Found {0} buttons total" -f $allButtons.Count)

# Filenames (ASCII only — avoids PS5 encoding hassles in the script source)
$files = @(
    'kw-01-students.png',
    'kw-02-subjects.png',
    'kw-03-knowledge.png',
    'kw-04-import.png',
    'kw-05-papers.png',
    'kw-06-mistakes.png',
    'kw-07-analysis.png',
    'kw-08-trends.png',
    'kw-09-settings.png')

$tmp = $env:TEMP
for ($i = 0; $i -lt 9; $i++) {
    if ($i -ge $allButtons.Count) { break }
    $btn = $allButtons.Item($i)
    $name = $btn.Current.Name
    Write-Host ("Navigating to {0} ({1})..." -f $i, $name)
    $invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    [void]$invoke.Invoke()
    Start-Sleep -Milliseconds 900
    Bring-To-Front
    Snap (Join-Path $tmp $files[$i])
}
