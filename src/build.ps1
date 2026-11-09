<#
  重新构建 bin\DshEdgeDock.exe
  用法：powershell -ExecutionPolicy Bypass -File .\src\build.ps1
  依赖：Windows 自带 .NET Framework 4.x 的 csc.exe（无需装 SDK）
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = 'C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

$out = Join-Path $root 'bin\DshEdgeDock.exe'
New-Item -ItemType Directory -Path (Join-Path $root 'bin') -Force | Out-Null

& $csc /nologo /target:winexe /optimize+ /out:$out `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  (Join-Path $PSScriptRoot 'EdgeDock.cs')
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$item = Get-Item $out
Write-Host ("已生成 {0}  {1:N0} 字节" -f $item.FullName, $item.Length)
Write-Host '下一步：把插件目录重新拷进 profile（或重跑 install.ps1），然后完全退出并重开 Harness。'
