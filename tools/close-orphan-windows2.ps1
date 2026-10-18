$ErrorActionPreference = 'Continue'
Add-Type -Namespace Orphan2 -Name Fix -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", SetLastError=true)] public static extern bool EndTask(IntPtr hWnd, bool fShutDown, bool fForce);
'@

$targets = @(10272, 29404)
$found = New-Object System.Collections.ArrayList
$cb = [Orphan2.Fix+EnumWindowsProc]{
    param($h, $l)
    if ([Orphan2.Fix]::IsWindowVisible($h)) {
        $pid2 = 0
        [void][Orphan2.Fix]::GetWindowThreadProcessId($h, [ref]$pid2)
        if ($targets -contains [int]$pid2) { [void]$found.Add([pscustomobject]@{ HWND = $h; PID = $pid2 }) }
    }
    return $true
}
[void][Orphan2.Fix]::EnumWindows($cb, [IntPtr]::Zero)
Write-Output ("待处理窗口: {0}" -f $found.Count)
foreach ($f in $found) {
    Write-Output ("EndTask HWND={0} (PID={1})" -f $f.HWND, $f.PID)
    $r = [Orphan2.Fix]::EndTask($f.HWND, $false, $true)
    Write-Output ("   -> {0}；窗口仍存在={1}" -f $r, [Orphan2.Fix]::IsWindow($f.HWND))
}
Write-Output "DONE"
