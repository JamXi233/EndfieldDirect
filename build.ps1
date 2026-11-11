$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "找不到 C# 编译器 / C# compiler not found: $csc"
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /win32manifest:src\app.manifest /out:EndfieldDirect.exe src\Program.cs src\AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw "编译失败 / Build failed (exit code $LASTEXITCODE)" }

Get-Item EndfieldDirect.exe | Select-Object Name, Length
