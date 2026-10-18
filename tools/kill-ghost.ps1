$ErrorActionPreference = 'Continue'
Add-Type -Namespace W -Name Ghost -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", SetLastError=true)] public static extern bool EndTask(IntPtr hWnd, bool fShutDown, bool fForce);
'@

$targets = New-Object System.Collections.ArrayList
$cb = [W.Ghost+EnumWindowsProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 512
    [void][W.Ghost]::GetWindowTextW($h, $sb, 512)
    $t = $sb.ToString()
    if ($t -like '*Touch+*') { [void]$targets.Add([pscustomobject]@{ HWND = $h; Title = $t; Visible = [W.Ghost]::IsWindowVisible($h) }) }
    return $true
}
[void][W.Ghost]::EnumWindows($cb, [IntPtr]::Zero)

Write-Output ("找到 {0} 个候选窗口" -f $targets.Count)
foreach ($t in $targets) {
    Write-Output ("处理 HWND={0} visible={1} : {2}" -f $t.HWND, $t.Visible, $t.Title)
    try {
        $r = [W.Ghost]::EndTask($t.HWND, $false, $true)
        Write-Output ("   EndTask 返回 {0}" -f $r)
    } catch {
        Write-Output ("   EndTask 异常: " + $_.Exception.Message)
    }
}
Start-Sleep -Milliseconds 800
foreach ($t in $targets) {
    Write-Output ("处理后 HWND={0} 还存在={1} 可见={2}" -f $t.HWND, [W.Ghost]::IsWindow($t.HWND), [W.Ghost]::IsWindowVisible($t.HWND))
}
Write-Output "完成"
