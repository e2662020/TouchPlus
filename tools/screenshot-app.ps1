param(
    [string]$Exe = 'C:\Users\RATE\SeewoTouch+\build\TouchPlus.exe',
    [string]$Out = 'C:\Users\RATE\SeewoTouch+\build\ui.png',
    [int]$Wait = 4,
    [int]$AutoClose = 60,
    [string]$ExtraArgs = '--safe --show'
)

Add-Type -Namespace Cap -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][Cap.Dpi]::SetProcessDPIAware()

Add-Type -Namespace Cap -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
public delegate bool EnumProc(IntPtr h, IntPtr lp);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

$argList = @()
$argList += $ExtraArgs.Split(' ')
$argList += @('--autoclose', "$AutoClose")

$p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
Start-Sleep -Seconds $Wait
$p.Refresh()

if ($p.HasExited) { "进程已退出，退出码 $($p.ExitCode)"; exit 1 }

# 枚举该进程所有可见顶层窗口，取面积最大的那个（WinForms 的隐藏辅助窗口会被排除）
$pid2 = $p.Id
$cands = New-Object System.Collections.ArrayList
$cb = [Cap.W+EnumProc]{
    param($h, $l)
    $wp = 0
    [void][Cap.W]::GetWindowThreadProcessId($h, [ref]$wp)
    if ($wp -eq $pid2 -and [Cap.W]::IsWindowVisible($h)) {
        $r = New-Object Cap.W+RECT
        [void][Cap.W]::GetWindowRect($h, [ref]$r)
        $sb = New-Object System.Text.StringBuilder 256
        [void][Cap.W]::GetClassNameW($h, $sb, 256)
        [void]$cands.Add([pscustomobject]@{HWND=$h; W=($r.Right-$r.Left); H=($r.Bottom-$r.Top); Class=$sb.ToString()})
    }
    return $true
}
[void][Cap.W]::EnumWindows($cb, [IntPtr]::Zero)
"该进程可见窗口："
$cands | Sort-Object -Property @{Expression={$_.W * $_.H}} -Descending | ForEach-Object { "  HWND=$($_.HWND) $($_.W)x$($_.H) $($_.Class)" }
$best = $cands | Sort-Object -Property @{Expression={$_.W * $_.H}} -Descending | Select-Object -First 1
if (-not $best) { "找不到可见窗口"; try { $p.Kill() } catch {}; exit 2 }
$hwnd = $best.HWND

"HWND = $hwnd  visible = $([Cap.W]::IsWindowVisible($hwnd))"
[void][Cap.W]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 600

$rect = New-Object Cap.W+RECT
[void][Cap.W]::GetWindowRect($hwnd, [ref]$rect)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
"窗口尺寸 = ${w}x${h}"

Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Cap.W]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"PrintWindow = $ok  已保存: $Out"

try { $p.CloseMainWindow() } catch { }
Start-Sleep -Milliseconds 500
try { if (-not $p.HasExited) { $p.Kill() } } catch { }
"已结束测试进程"
