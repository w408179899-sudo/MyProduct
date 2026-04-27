Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = baseDir
cmd = "powershell.exe -ExecutionPolicy Bypass -File " & Chr(34) & fso.BuildPath(baseDir, "LaunchAvePoint.ps1") & Chr(34)
shell.Run cmd, 0, False
