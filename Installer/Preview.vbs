' Run with wscript.exe Preview.vbs <absolute MSI path> [dialog name].
' Windows Installer authoring preview only; no installation actions execute.
If WScript.Arguments.Count = 0 Then WScript.Quit 1
Set installer = CreateObject("WindowsInstaller.Installer")
Set database = installer.OpenDatabase(WScript.Arguments(0), 0)
Set preview = database.EnableUIPreview
dialog = "Completed"
If WScript.Arguments.Count > 1 Then dialog = WScript.Arguments(1)
preview.ViewDialog dialog
WScript.Sleep 90000
preview.ViewDialog ""
