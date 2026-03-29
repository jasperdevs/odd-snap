$sm = 'C:\Users\bunny\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Yoink'
New-Item -ItemType Directory -Force -Path $sm | Out-Null

$ws = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $sm 'Yoink.lnk'
$s = $ws.CreateShortcut($shortcutPath)
$s.TargetPath = 'C:\Users\bunny\AppData\Local\Programs\Yoink\Yoink.exe'
$s.WorkingDirectory = 'C:\Users\bunny\AppData\Local\Programs\Yoink'
$s.IconLocation = 'C:\Users\bunny\AppData\Local\Programs\Yoink\Yoink.exe,0'
$s.Save()

Get-ChildItem $sm | Select-Object FullName | Format-Table -AutoSize
