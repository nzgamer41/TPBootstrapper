(Get-Content .\genautoupdate.ps1).replace('X.X.X.X',$env:appveyor_build_version) | Set-Content .\genautoupdateworking.ps1
.\genautoupdateworking.ps1
Remove-Item .\genautoupdateworking.ps1