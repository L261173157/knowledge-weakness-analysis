param(
    [Parameter(Mandatory=$true)][string]$OutPath
)

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
if (-not $proc) { throw "App not running" }
$hwnd = $proc.MainWindowHandle

[void][Win32]::ShowWindow($hwnd, 9)            # SW_RESTORE
$HWND_TOP = [IntPtr]::Zero
[void][Win32]::SetWindowPos($hwnd, $HWND_TOP, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) # NOSIZE|NOMOVE|SHOWWINDOW
[void][Win32]::BringWindowToTop($hwnd)
[void][Win32]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 800

$rect = New-Object Win32+RECT
[void][Win32]::GetWindowRect($hwnd, [ref]$rect)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# PW_RENDERFULLCONTENT (0x00000002) tells DWM to render layered/GPU content too (needed for Avalonia / WebView etc.)
$ok = [Win32]::PrintWindow($hwnd, $hdc, 0x00000002)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Saved $OutPath ($w x $h) PrintWindow=$ok"
