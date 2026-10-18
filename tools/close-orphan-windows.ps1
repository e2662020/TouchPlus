$ErrorActionPreference = 'Continue'
Add-Type -Namespace Orphan -Name Fix -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", SetLastError=true)] public static extern bool EndTask(IntPtr hWnd, bool fShutDown, bool fForce);
'@

$orphans = New-Object System.Collections.ArrayList
$cb = [Orphan.Fix+EnumWindowsProc]{
    param($h, $l)
    if ([Orphan.Fix]::IsWindowVisible($h)) {
        $pid2 = 0
        [void][Orphan.Fix]::GetWindowThreadProcessId($h, [ref]$pid2)
        $alive = $true
        try { [void](Get-Process -Id $pid2 -ErrorAction Stop) } catch { $alive = $false }
        if (-not $alive) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][Orphan.Fix]::GetClassNameW($h, $sb, 256)
            [void]$orphans.Add([pscustomobject]@{ HWND = $h; PID = $pid2; Class = $sb.ToString() })
        }
    }
    return $true
}
[void][Orphan.Fix]::EnumWindows($cb, [IntPtr]::Zero)

Write-Output ("孤儿窗口数量: {0}" -f $orphans.Count)
foreach ($o in $orphans) {
    Write-Output ("处理 HWND={0} (原PID={1}) class={2}" -f $o.HWND, $o.PID, $o.Class)
    try {
        $r = [Orphan.Fix]::EndTask($o.HWND, $false, $true)
        Write-Output ("   EndTask -> {0}" -f $r)
    } catch {
        Write-Output ("   EndTask 异常: " + $_.Exception.Message)
    }
    Start-Sleep -Milliseconds 400
    Write-Output ("   之后 窗口仍存在={0}" -f [Orphan.Fix]::IsWindow($o.HWND))
}
Write-Output "DONE"
