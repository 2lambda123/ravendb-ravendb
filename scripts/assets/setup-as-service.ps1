#Requires -Version 4.0
#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop";
$ProgressPreference = "SilentlyContinue";

function Get-ScriptDirectory
{
    $Invocation = (Get-Variable MyInvocation -Scope 1).Value;
    Split-Path $Invocation.MyCommand.Path;
}

function CheckPortIsClosed($port) {
    $result = Test-NetConnection -Port $port -ComputerName 127.0.0.1 -InformationLevel Quiet 3> $null
    return $result -eq $false
}

function SetAclOnServerDirectory($dir) {
    $acl = Get-Acl $dir
    $permissions = "LocalService", "FullControl", "ContainerInherit, ObjectInherit", "None", "Allow"
    $rule = New-Object -TypeName System.Security.AccessControl.FileSystemAccessRule -ArgumentList $permissions
    $acl.SetAccessRuleProtection($False, $False)
    $acl.AddAccessRule($rule)
    Set-Acl -Path $dir -AclObject $acl
}

$scriptDirectory = Get-ScriptDirectory;
$settingsTemplateJson = "settings.default.json";
$settingsJson = "settings.json";
$rvn = "rvn.exe";
$serverDir = Join-Path $scriptDirectory "Server"

SetAclOnServerDirectory $(Join-Path -Path $scriptDirectory -ChildPath "Server")

$settingsJsonPath = Join-Path $serverDir $settingsJson
$settingsTemplateJsonPath = Join-Path $serverDir $settingsTemplateJson;

$name = 'RavenDB'
 
$isAlreadyConfigured = Test-Path $settingsJsonPath

if ($isAlreadyConfigured) {
    write-host "Server was run before - attempt to use existing configuration."
    $serverUrl = $(Get-Content $settingsJsonPath -raw | ConvertFrom-Json).ServerUrl
} else {
    write-host "Server run for the first time."
    $secure = Read-Host -Prompt 'Would you like to setup a secure server? (y/n)'

    if ($secure -match '^\s*?[yY]') {
        $port = 443
    }
    else {
        $port = 8080
    }

    if ($port -lt 0 -Or $port -gt 65535) {
        Write-Error "Error. Port must be in the range 0-65535."
        exit 1
    }

    if ((CheckPortIsClosed $port) -eq $false) {
        Write-Error "Port $port is not available.";
        exit 2
    }

    try {
        $json = Get-Content $settingsTemplateJsonPath -raw | ConvertFrom-Json
        $serverUrl = $json.ServerUrl = "http://127.0.0.1:$port"
        $json | ConvertTo-Json  | Set-Content $settingsTemplateJsonPath
    }
    catch {
        Write-Error $_.Exception
        exit 3
    }
}

Push-Location $serverDir;

Try
{
    Invoke-Expression -Command ".\$rvn windows-service register --service-name $name";
    Start-Service -Name $name
}
catch
{
    write-error $_.Exception
    exit 4
}
finally
{
    Pop-Location;
}

Write-Host "Service started, server listening on $serverUrl."
Write-Host "You can now finish setting up the RavenDB service in the browser."

Start-Sleep -Seconds 3
Start-Process $serverUrl 

# SIG # Begin signature block
# MIIfUwYJKoZIhvcNAQcCoIIfRDCCH0ACAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUByfLMvgTa8Nqf3gGop5vUbUo
# 6mGgghk6MIIFQjCCBCqgAwIBAgIRAMI1UF0BgwiHX2Zhtl3j7dIwDQYJKoZIhvcN
# AQELBQAwfDELMAkGA1UEBhMCR0IxGzAZBgNVBAgTEkdyZWF0ZXIgTWFuY2hlc3Rl
# cjEQMA4GA1UEBxMHU2FsZm9yZDEYMBYGA1UEChMPU2VjdGlnbyBMaW1pdGVkMSQw
# IgYDVQQDExtTZWN0aWdvIFJTQSBDb2RlIFNpZ25pbmcgQ0EwHhcNMjAwMjE4MDAw
# MDAwWhcNMjMwMjE3MjM1OTU5WjCBiDELMAkGA1UEBhMCSUwxEDAOBgNVBBEMBzM4
# MjAzNDkxDzANBgNVBAcMBkhhZGVyYTEUMBIGA1UECQwLOSBBaGFkIEhhYW0xHzAd
# BgNVBAoMFkhJQkVSTkFUSU5HIFJISU5PUyBMVEQxHzAdBgNVBAMMFkhJQkVSTkFU
# SU5HIFJISU5PUyBMVEQwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQD3
# fsfnnxHgDTPTYRkc3XFLh7xedZ3gx//h4bF+Doz/x4wiYt9jE3CpAWES2dlN8j24
# S55L81cjNSmsjrNmPnjcdAiQ5y9jW8nkszeJiZWAcISeqwQI/QxMAviEhsNenh4g
# UeIwaODG18/Pt/kBHbDhzXswDCatNiHYr9Kg9e6RQKzpcqyUJia1+ZLWRIjQmMVQ
# f69lsFivxLrJLOU1Cemnyf+4vxJFOFFv3jWr3uakiKkAQjJyF4Rumm9BSJjKOmzD
# ke4ZFoHqPUJ8PZnpNMUAC8QDiZBWRf4+fhO7h5vvDATwrsSwfy/r7wbBtdv9Nd8h
# XBwEK0BYRnpSYShuZ8KnAgMBAAGjggGwMIIBrDAfBgNVHSMEGDAWgBQO4TqoUzox
# 1Yq+wbutZxoDha00DjAdBgNVHQ4EFgQUUkdHoUxWK2neV5Tm5GaRC1Qf36owDgYD
# VR0PAQH/BAQDAgeAMAwGA1UdEwEB/wQCMAAwEwYDVR0lBAwwCgYIKwYBBQUHAwMw
# EQYJYIZIAYb4QgEBBAQDAgQQMEAGA1UdIAQ5MDcwNQYMKwYBBAGyMQECAQMCMCUw
# IwYIKwYBBQUHAgEWF2h0dHBzOi8vc2VjdGlnby5jb20vQ1BTMEMGA1UdHwQ8MDow
# OKA2oDSGMmh0dHA6Ly9jcmwuc2VjdGlnby5jb20vU2VjdGlnb1JTQUNvZGVTaWdu
# aW5nQ0EuY3JsMHMGCCsGAQUFBwEBBGcwZTA+BggrBgEFBQcwAoYyaHR0cDovL2Ny
# dC5zZWN0aWdvLmNvbS9TZWN0aWdvUlNBQ29kZVNpZ25pbmdDQS5jcnQwIwYIKwYB
# BQUHMAGGF2h0dHA6Ly9vY3NwLnNlY3RpZ28uY29tMCgGA1UdEQQhMB+BHXN1cHBv
# cnRAaGliZXJuYXRpbmdyaGlub3MuY29tMA0GCSqGSIb3DQEBCwUAA4IBAQCEhB4L
# 4nlCvr9vto0rtqBhi1M8EarmgL7IxRj0hHc0q0KZhl+tbL8Hc/0dX07A5BXYD59x
# VQvA0iH21fyTzA6nd4tw6Sy5bfFA/CccTcQdC+9SZ2BY6AQm8czNxCe7nBP2AJq9
# EVKINjaUZFHHJoJZfJr62fQq6radC5OAoKJ2DXnP6ya400KTuQhVVmXNEE8puso7
# eCJV7C/z49FyN3fnY0PawXMRjHhX0jbYmZcNB/q6vjFyUYDkAFlyeQ8puLe+Jgsn
# atxKoXwihi2Ck82EyI5B8XEXpi8/qeMDU/U1kOV9t2tYlodsYy8dTXlC01HezrT2
# gY8268JKg+PA4hl5MIIF9TCCA92gAwIBAgIQHaJIMG+bJhjQguCWfTPTajANBgkq
# hkiG9w0BAQwFADCBiDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCk5ldyBKZXJzZXkx
# FDASBgNVBAcTC0plcnNleSBDaXR5MR4wHAYDVQQKExVUaGUgVVNFUlRSVVNUIE5l
# dHdvcmsxLjAsBgNVBAMTJVVTRVJUcnVzdCBSU0EgQ2VydGlmaWNhdGlvbiBBdXRo
# b3JpdHkwHhcNMTgxMTAyMDAwMDAwWhcNMzAxMjMxMjM1OTU5WjB8MQswCQYDVQQG
# EwJHQjEbMBkGA1UECBMSR3JlYXRlciBNYW5jaGVzdGVyMRAwDgYDVQQHEwdTYWxm
# b3JkMRgwFgYDVQQKEw9TZWN0aWdvIExpbWl0ZWQxJDAiBgNVBAMTG1NlY3RpZ28g
# UlNBIENvZGUgU2lnbmluZyBDQTCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoC
# ggEBAIYijTKFehifSfCWL2MIHi3cfJ8Uz+MmtiVmKUCGVEZ0MWLFEO2yhyemmcuV
# MMBW9aR1xqkOUGKlUZEQauBLYq798PgYrKf/7i4zIPoMGYmobHutAMNhodxpZW0f
# bieW15dRhqb0J+V8aouVHltg1X7XFpKcAC9o95ftanK+ODtj3o+/bkxBXRIgCFno
# Oc2P0tbPBrRXBbZOoT5Xax+YvMRi1hsLjcdmG0qfnYHEckC14l/vC0X/o84Xpi1V
# sLewvFRqnbyNVlPG8Lp5UEks9wO5/i9lNfIi6iwHr0bZ+UYc3Ix8cSjz/qfGFN1V
# kW6KEQ3fBiSVfQ+noXw62oY1YdMCAwEAAaOCAWQwggFgMB8GA1UdIwQYMBaAFFN5
# v1qqK0rPVIDh2JvAnfKyA2bLMB0GA1UdDgQWBBQO4TqoUzox1Yq+wbutZxoDha00
# DjAOBgNVHQ8BAf8EBAMCAYYwEgYDVR0TAQH/BAgwBgEB/wIBADAdBgNVHSUEFjAU
# BggrBgEFBQcDAwYIKwYBBQUHAwgwEQYDVR0gBAowCDAGBgRVHSAAMFAGA1UdHwRJ
# MEcwRaBDoEGGP2h0dHA6Ly9jcmwudXNlcnRydXN0LmNvbS9VU0VSVHJ1c3RSU0FD
# ZXJ0aWZpY2F0aW9uQXV0aG9yaXR5LmNybDB2BggrBgEFBQcBAQRqMGgwPwYIKwYB
# BQUHMAKGM2h0dHA6Ly9jcnQudXNlcnRydXN0LmNvbS9VU0VSVHJ1c3RSU0FBZGRU
# cnVzdENBLmNydDAlBggrBgEFBQcwAYYZaHR0cDovL29jc3AudXNlcnRydXN0LmNv
# bTANBgkqhkiG9w0BAQwFAAOCAgEATWNQ7Uc0SmGk295qKoyb8QAAHh1iezrXMsL2
# s+Bjs/thAIiaG20QBwRPvrjqiXgi6w9G7PNGXkBGiRL0C3danCpBOvzW9Ovn9xWV
# M8Ohgyi33i/klPeFM4MtSkBIv5rCT0qxjyT0s4E307dksKYjalloUkJf/wTr4XRl
# eQj1qZPea3FAmZa6ePG5yOLDCBaxq2NayBWAbXReSnV+pbjDbLXP30p5h1zHQE1j
# NfYw08+1Cg4LBH+gS667o6XQhACTPlNdNKUANWlsvp8gJRANGftQkGG+OY96jk32
# nw4e/gdREmaDJhlIlc5KycF/8zoFm/lv34h/wCOe0h5DekUxwZxNqfBZslkZ6GqN
# KQQCd3xLS81wvjqyVVp4Pry7bwMQJXcVNIr5NsxDkuS6T/FikyglVyn7URnHoSVA
# aoRXxrKdsbwcCtp8Z359LukoTBh+xHsxQXGaSynsCz1XUNLK3f2eBVHlRHjdAd6x
# dZgNVCT98E7j4viDvXK6yz067vBeF5Jobchh+abxKgoLpbn0nu6YMgWFnuv5gynT
# xix9vTp3Los3QqBqgu07SqqUEKThDfgXxbZaeTMYkuO1dfih6Y4KJR7kHvGfWocj
# /5+kUZ77OYARzdu1xKeogG/lU9Tg46LC0lsa+jImLWpXcBw8pFguo/NbSwfcMlnz
# h6cabVgwggbsMIIE1KADAgECAhAwD2+s3WaYdHypRjaneC25MA0GCSqGSIb3DQEB
# DAUAMIGIMQswCQYDVQQGEwJVUzETMBEGA1UECBMKTmV3IEplcnNleTEUMBIGA1UE
# BxMLSmVyc2V5IENpdHkxHjAcBgNVBAoTFVRoZSBVU0VSVFJVU1QgTmV0d29yazEu
# MCwGA1UEAxMlVVNFUlRydXN0IFJTQSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTAe
# Fw0xOTA1MDIwMDAwMDBaFw0zODAxMTgyMzU5NTlaMH0xCzAJBgNVBAYTAkdCMRsw
# GQYDVQQIExJHcmVhdGVyIE1hbmNoZXN0ZXIxEDAOBgNVBAcTB1NhbGZvcmQxGDAW
# BgNVBAoTD1NlY3RpZ28gTGltaXRlZDElMCMGA1UEAxMcU2VjdGlnbyBSU0EgVGlt
# ZSBTdGFtcGluZyBDQTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAMgb
# Aa/ZLH6ImX0BmD8gkL2cgCFUk7nPoD5T77NawHbWGgSlzkeDtevEzEk0y/NFZbn5
# p2QWJgn71TJSeS7JY8ITm7aGPwEFkmZvIavVcRB5h/RGKs3EWsnb111JTXJWD9zJ
# 41OYOioe/M5YSdO/8zm7uaQjQqzQFcN/nqJc1zjxFrJw06PE37PFcqwuCnf8DZRS
# t/wflXMkPQEovA8NT7ORAY5unSd1VdEXOzQhe5cBlK9/gM/REQpXhMl/VuC9RpyC
# vpSdv7QgsGB+uE31DT/b0OqFjIpWcdEtlEzIjDzTFKKcvSb/01Mgx2Bpm1gKVPQF
# 5/0xrPnIhRfHuCkZpCkvRuPd25Ffnz82Pg4wZytGtzWvlr7aTGDMqLufDRTUGMQw
# mHSCIc9iVrUhcxIe/arKCFiHd6QV6xlV/9A5VC0m7kUaOm/N14Tw1/AoxU9kgwLU
# ++Le8bwCKPRt2ieKBtKWh97oaw7wW33pdmmTIBxKlyx3GSuTlZicl57rjsF4VsZE
# Jd8GEpoGLZ8DXv2DolNnyrH6jaFkyYiSWcuoRsDJ8qb/fVfbEnb6ikEk1Bv8cqUU
# otStQxykSYtBORQDHin6G6UirqXDTYLQjdprt9v3GEBXc/Bxo/tKfUU2wfeNgvq5
# yQ1TgH36tjlYMu9vGFCJ10+dM70atZ2h3pVBeqeDAgMBAAGjggFaMIIBVjAfBgNV
# HSMEGDAWgBRTeb9aqitKz1SA4dibwJ3ysgNmyzAdBgNVHQ4EFgQUGqH4YRkgD8NB
# d0UojtE1XwYSBFUwDgYDVR0PAQH/BAQDAgGGMBIGA1UdEwEB/wQIMAYBAf8CAQAw
# EwYDVR0lBAwwCgYIKwYBBQUHAwgwEQYDVR0gBAowCDAGBgRVHSAAMFAGA1UdHwRJ
# MEcwRaBDoEGGP2h0dHA6Ly9jcmwudXNlcnRydXN0LmNvbS9VU0VSVHJ1c3RSU0FD
# ZXJ0aWZpY2F0aW9uQXV0aG9yaXR5LmNybDB2BggrBgEFBQcBAQRqMGgwPwYIKwYB
# BQUHMAKGM2h0dHA6Ly9jcnQudXNlcnRydXN0LmNvbS9VU0VSVHJ1c3RSU0FBZGRU
# cnVzdENBLmNydDAlBggrBgEFBQcwAYYZaHR0cDovL29jc3AudXNlcnRydXN0LmNv
# bTANBgkqhkiG9w0BAQwFAAOCAgEAbVSBpTNdFuG1U4GRdd8DejILLSWEEbKw2yp9
# KgX1vDsn9FqguUlZkClsYcu1UNviffmfAO9Aw63T4uRW+VhBz/FC5RB9/7B0H4/G
# XAn5M17qoBwmWFzztBEP1dXD4rzVWHi/SHbhRGdtj7BDEA+N5Pk4Yr8TAcWFo0zF
# zLJTMJWk1vSWVgi4zVx/AZa+clJqO0I3fBZ4OZOTlJux3LJtQW1nzclvkD1/RXLB
# GyPWwlWEZuSzxWYG9vPWS16toytCiiGS/qhvWiVwYoFzY16gu9jc10rTPa+DBjgS
# HSSHLeT8AtY+dwS8BDa153fLnC6NIxi5o8JHHfBd1qFzVwVomqfJN2Udvuq82EKD
# QwWli6YJ/9GhlKZOqj0J9QVst9JkWtgqIsJLnfE5XkzeSD2bNJaaCV+O/fexUpHO
# P4n2HKG1qXUfcb9bQ11lPVCBbqvw0NP8srMftpmWJvQ8eYtcZMzN7iea5aDADHKH
# wW5NWtMe6vBE5jJvHOsXTpTDeGUgOw9Bqh/poUGd/rG4oGUqNODeqPk85sEwu8Cg
# Yyz8XBYAqNDEf+oRnR4GxqZtMl20OAkrSQeq/eww2vGnL8+3/frQo4TZJ577AWZ3
# uVYQ4SBuxq6x+ba6yDVdM3aO8XwgDCp3rrWiAoa6Ke60WgCxjKvj+QrJVF3UuWp0
# nr1IrpgwggcHMIIE76ADAgECAhEAjHegAI/00bDGPZ86SIONazANBgkqhkiG9w0B
# AQwFADB9MQswCQYDVQQGEwJHQjEbMBkGA1UECBMSR3JlYXRlciBNYW5jaGVzdGVy
# MRAwDgYDVQQHEwdTYWxmb3JkMRgwFgYDVQQKEw9TZWN0aWdvIExpbWl0ZWQxJTAj
# BgNVBAMTHFNlY3RpZ28gUlNBIFRpbWUgU3RhbXBpbmcgQ0EwHhcNMjAxMDIzMDAw
# MDAwWhcNMzIwMTIyMjM1OTU5WjCBhDELMAkGA1UEBhMCR0IxGzAZBgNVBAgTEkdy
# ZWF0ZXIgTWFuY2hlc3RlcjEQMA4GA1UEBxMHU2FsZm9yZDEYMBYGA1UEChMPU2Vj
# dGlnbyBMaW1pdGVkMSwwKgYDVQQDDCNTZWN0aWdvIFJTQSBUaW1lIFN0YW1waW5n
# IFNpZ25lciAjMjCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAJGHSyyL
# wfEeoJ7TB8YBylKwvnl5XQlmBi0vNX27wPsn2kJqWRslTOrvQNaafjLIaoF9tFw+
# VhCBNToiNoz7+CAph6x00BtivD9khwJf78WA7wYc3F5Ok4e4mt5MB06FzHDFDXvs
# w9njl+nLGdtWRWzuSyBsyT5s/fCb8Sj4kZmq/FrBmoIgOrfv59a4JUnCORuHgTnL
# w7c6zZ9QBB8amaSAAk0dBahV021SgIPmbkilX8GJWGCK7/GszYdjGI50y4SHQWlj
# gbz2H6p818FBzq2rdosggNQtlQeNx/ULFx6a5daZaVHHTqadKW/neZMNMmNTrszG
# KYogwWDG8gIsxPnIIt/5J4Khg1HCvMmCGiGEspe81K9EHJaCIpUqhVSu8f0+SXR0
# /I6uP6Vy9MNaAapQpYt2lRtm6+/a35Qu2RrrTCd9TAX3+CNdxFfIJgV6/IEjX1QJ
# OCpi1arK3+3PU6sf9kSc1ZlZxVZkW/eOUg9m/Jg/RAYTZG7p4RVgUKWx7M+46MkL
# vsWE990Kndq8KWw9Vu2/eGe2W8heFBy5r4Qtd6L3OZU3b05/HMY8BNYxxX7vPehR
# fnGtJHQbLNz5fKrvwnZJaGLVi/UD3759jg82dUZbk3bEg+6CviyuNxLxvFbD5K1D
# w7dmll6UMvqg9quJUPrOoPMIgRrRRKfM97gxAgMBAAGjggF4MIIBdDAfBgNVHSME
# GDAWgBQaofhhGSAPw0F3RSiO0TVfBhIEVTAdBgNVHQ4EFgQUaXU3e7udNUJOv1fT
# mtufAdGu3tAwDgYDVR0PAQH/BAQDAgbAMAwGA1UdEwEB/wQCMAAwFgYDVR0lAQH/
# BAwwCgYIKwYBBQUHAwgwQAYDVR0gBDkwNzA1BgwrBgEEAbIxAQIBAwgwJTAjBggr
# BgEFBQcCARYXaHR0cHM6Ly9zZWN0aWdvLmNvbS9DUFMwRAYDVR0fBD0wOzA5oDeg
# NYYzaHR0cDovL2NybC5zZWN0aWdvLmNvbS9TZWN0aWdvUlNBVGltZVN0YW1waW5n
# Q0EuY3JsMHQGCCsGAQUFBwEBBGgwZjA/BggrBgEFBQcwAoYzaHR0cDovL2NydC5z
# ZWN0aWdvLmNvbS9TZWN0aWdvUlNBVGltZVN0YW1waW5nQ0EuY3J0MCMGCCsGAQUF
# BzABhhdodHRwOi8vb2NzcC5zZWN0aWdvLmNvbTANBgkqhkiG9w0BAQwFAAOCAgEA
# SgN4kEIz7Hsagwk2M5hVu51ABjBrRWrxlA4ZUP9bJV474TnEW7rplZA3N73f+2Ts
# 5YK3lcxXVXBLTvSoh90ihaZXu7ghJ9SgKjGUigchnoq9pxr1AhXLRFCZjOw+ugN3
# poICkMIuk6m+ITR1Y7ngLQ/PATfLjaL6uFqarqF6nhOTGVWPCZAu3+qIFxbradbh
# Jb1FCJeA11QgKE/Ke7OzpdIAsGA0ZcTjxcOl5LqFqnpp23WkPnlomjaLQ6421GFy
# PA6FYg2gXnDbZC8Bx8GhxySUo7I8brJeotD6qNG4JRwW5sDVf2gaxGUpNSotiLzq
# rnTWgufAiLjhT3jwXMrAQFzCn9UyHCzaPKw29wZSmqNAMBewKRaZyaq3iEn36Asl
# M7U/ba+fXwpW3xKxw+7OkXfoIBPpXCTH6kQLSuYThBxN6w21uIagMKeLoZ+0LMzA
# FiPJkeVCA0uAzuRN5ioBPsBehaAkoRdA1dvb55gQpPHqGRuAVPpHieiYgal1wA7f
# 0GiUeaGgno62t0Jmy9nZay9N2N4+Mh4g5OycTUKNncczmYI3RNQmKSZAjngvue76
# L/Hxj/5QuHjdFJbeHA5wsCqFarFsaOkq5BArbiH903ydN+QqBtbD8ddo408HeYEI
# E/6yZF7psTzm0Hgjsgks4iZivzupl1HMx0QygbKvz98xggWDMIIFfwIBATCBkTB8
# MQswCQYDVQQGEwJHQjEbMBkGA1UECBMSR3JlYXRlciBNYW5jaGVzdGVyMRAwDgYD
# VQQHEwdTYWxmb3JkMRgwFgYDVQQKEw9TZWN0aWdvIExpbWl0ZWQxJDAiBgNVBAMT
# G1NlY3RpZ28gUlNBIENvZGUgU2lnbmluZyBDQQIRAMI1UF0BgwiHX2Zhtl3j7dIw
# CQYFKw4DAhoFAKB4MBgGCisGAQQBgjcCAQwxCjAIoAKAAKECgAAwGQYJKoZIhvcN
# AQkDMQwGCisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEOMAwGCisGAQQBgjcCARUw
# IwYJKoZIhvcNAQkEMRYEFJbBXJnkGFroHQ9AjkzE+4LcYlMcMA0GCSqGSIb3DQEB
# AQUABIIBACxX7mVwr2t/LFL5lJ9q4YNYs0ZADt3nKIeXtwJzLhLuvES2oM7Tyrih
# 0ym5evHeDhqIpI1dN7Jwukn7up7UrnRpeWRae+J/QjXOuX5nTN0RGuCHFbmk5drp
# VhDofc8Vv4fdDiem6sSpm9CK2OzzpwncvKJqDZKmXKBerY9Tq8JY+8DGSYrstEas
# h5s5Pl25yLK3TE/1dd54YqFriPz0GVn9Zl0Iaoq9a/rUKk0PWZJDcA3fQvlsPzrd
# uHsZ/62s25dlY5BEOFCbG6rd4JrPapuw3cEb5GqB22OzIIW4D8zk/7A2zTZRoGq5
# KKkCP24LTgo/uPdYG7BN852bfrf4B5ihggNMMIIDSAYJKoZIhvcNAQkGMYIDOTCC
# AzUCAQEwgZIwfTELMAkGA1UEBhMCR0IxGzAZBgNVBAgTEkdyZWF0ZXIgTWFuY2hl
# c3RlcjEQMA4GA1UEBxMHU2FsZm9yZDEYMBYGA1UEChMPU2VjdGlnbyBMaW1pdGVk
# MSUwIwYDVQQDExxTZWN0aWdvIFJTQSBUaW1lIFN0YW1waW5nIENBAhEAjHegAI/0
# 0bDGPZ86SIONazANBglghkgBZQMEAgIFAKB5MBgGCSqGSIb3DQEJAzELBgkqhkiG
# 9w0BBwEwHAYJKoZIhvcNAQkFMQ8XDTIyMDMxNjEzNDAzN1owPwYJKoZIhvcNAQkE
# MTIEMAoJ4GZd3OPSSnA5d6rMvDr5HdjlKkYxf40vASWcJAEdOuiwIMs5iZi9gKr4
# Kljf+DANBgkqhkiG9w0BAQEFAASCAgCNVuhn3oIcMu0zE3A/iLs3FUsaTCimx2ee
# ObrYvTvptI2hoIH/BAvqNk/Rth54WpyHX+KKbCTEfJQrtwvn63wS3+Bt285RIfHB
# bW/Wiv4R1P8ZGWMxe5tXFvEYe1avPKMVabKRJh0epp0S5MNRtC2TcNjoEYMpXYHM
# pW8Opq8lLDGITsygnA0vVGHER2rDbWuAhzEeEk6cpuYYLBNjlaafutUrPozBSM+w
# dtWiJ+g+YqRiXnbndk9N76vlnaksm4QY4z+eSCl8tchNgNJgb8fa5b/I96IKeEpf
# AYPNooi3BFS+xDtIYSiV/8NqS7Dz5fPjUeAyByZXX7umbaH1Ddy7Lm2r1aes7FPe
# c6x5hFnd2+vYx78LD/1Lg9nrrKaf1lULIau+c/+lw1iSbtGA8WfD0OfA+K7AXq9m
# tJkqv41NGL+OTn8ifyd7vLojoexXsh5jiRXXX08eioLkaFIM5GBVTK+qFRrHcPK/
# 6RnuoC6gkukkqIyPNbhiFCMkC3elMI9NwCmFuK7EF8hw+EZQpYokwy4jwQ+NUnve
# n3uKXjH3SeLm2vs1yWHGdzbPd+Rf3aapJSpMwgt5HPzLnGzcxUt9ezyyYm/w5IvA
# cysTB8xjfLJNW//HPHNVjODCxpSZIJGvgNIou6WY6Ms1DM3apU3MhivTPJ3XYP1a
# UVjPJb6eJA==
# SIG # End signature block
