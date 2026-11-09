<#
  贴边自动收起（dsh-edge-dock）安装 / 迁移脚本
  用法：powershell -ExecutionPolicy Bypass -File .\install.ps1
        可加 -Profile "D:\xxx\.dsh\profiles\desktop" 指定 profile
  做的事：把本目录拷进 profile 的 plugins\ 与 node_modules\，
          并在 profile 的 package.json 里补 dependencies 与 dsh.profile.bundles 两处登记
          （按文本插入，只加两行，其余格式与内容原样不动）。
#>
param(
  [string]$Profile = (Join-Path $env:USERPROFILE '.dsh\profiles\desktop')
)

$ErrorActionPreference = 'Stop'
$pkgName = 'dsh-edge-dock'
$src = $PSScriptRoot

if (-not (Test-Path (Join-Path $src 'package.json'))) { throw "找不到 package.json，请在插件目录里运行本脚本：$src" }
if (-not (Test-Path (Join-Path $src 'bin\DshEdgeDock.exe'))) { throw "找不到 bin\DshEdgeDock.exe，插件包不完整：$src" }
if (-not (Test-Path (Join-Path $Profile 'package.json'))) { throw "找不到 profile 的 package.json：$Profile（用 -Profile 指定正确路径）" }

# 1. 拷贝到两处（profile 用 hoisted linker，file: 依赖不会自动建链接）
$targets = @(
  (Join-Path $Profile "plugins\$pkgName"),
  (Join-Path $Profile "node_modules\$pkgName")
)
foreach ($t in $targets) {
  if (Test-Path $t) { Remove-Item $t -Recurse -Force }
  New-Item -ItemType Directory -Path $t -Force | Out-Null
  Copy-Item (Join-Path $src '*') $t -Recurse -Force
  Write-Host "已部署 -> $t"
}

# 2. 补 package.json 两处登记（文本插入，保留原有格式）
$pkgPath = Join-Path $Profile 'package.json'
Copy-Item $pkgPath "$pkgPath.bak" -Force
$raw = Get-Content $pkgPath -Raw -Encoding UTF8
$json = $raw | ConvertFrom-Json

$changed = @()
if (-not $json.dependencies.PSObject.Properties[$pkgName]) {
  $depLine = '"' + $pkgName + '": "file:./plugins/' + $pkgName + '",'
  $raw = [regex]::Replace($raw, '("dependencies"\s*:\s*\{)', ('$1' + "`r`n    " + $depLine), 1)
  $changed += 'dependencies'
}
if (-not ($json.dsh.profile.bundles -contains $pkgName)) {
  $bundleLine = '"' + $pkgName + '",'
  $raw = [regex]::Replace($raw, '("bundles"\s*:\s*\[)', ('$1' + "`r`n        " + $bundleLine), 1)
  $changed += 'dsh.profile.bundles'
}

if ($changed.Count -gt 0) {
  Set-Content $pkgPath $raw -Encoding UTF8 -NoNewline
  # 校验：能解析、两处都在，否则回滚
  try {
    $check = (Get-Content $pkgPath -Raw -Encoding UTF8) | ConvertFrom-Json
    if (-not $check.dependencies.PSObject.Properties[$pkgName]) { throw 'dependencies 未写入' }
    if (-not ($check.dsh.profile.bundles -contains $pkgName)) { throw 'bundles 未写入' }
  } catch {
    Copy-Item "$pkgPath.bak" $pkgPath -Force
    throw "写 package.json 失败，已还原为 .bak：$($_.Exception.Message)"
  }
  Write-Host ("已登记 package.json：" + ($changed -join '、'))
} else {
  Write-Host 'package.json 里已经登记过，未改动'
}

Write-Host ''
Write-Host '装好了。下一步：完全退出 DeepSeek Harness 再重新打开（托盘图标右键退出），'
Write-Host '然后在 设置 -> 贴边自动收起 里打开开关；"开机自动启动"需要在新机器上重新勾一次。'
