# dsh-edge-dock

把 [DeepSeek Harness](https://github.com/deepseek-ai) 贴到屏幕左/右边缘后，**点窗口外面它自动收进边缘，鼠标碰到那条边它自动滑出来**。
另带「窗口置顶」和「其他窗口全屏时也能唤出」。

只支持 **Windows + DSH 桌面版**。不需要管理员权限，不需要装 .NET SDK（Win10/11 自带 .NET Framework 4.x）。

设置入口：Harness 设置 → **贴边自动收起**（左侧导航，图标是一个靠右边的窗口）。

---

## 安装

1. 下载/克隆本仓库，解压到任意目录
2. 在该目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

脚本会：

- 把插件拷进 `%USERPROFILE%\.dsh\profiles\desktop\plugins\dsh-edge-dock` 与 `...\node_modules\dsh-edge-dock`
- 在 profile 的 `package.json` 里补两处登记（`dependencies` 与 `dsh.profile.bundles`），**只插入两行**，改前自动备份 `.bak`，写完自校验、失败自动还原

3. **完全退出** DeepSeek Harness（托盘图标右键退出）再重新打开
4. 打开 设置 → 贴边自动收起 → 打开「启用贴边自动收起」

profile 不在默认位置时加参数：`-Profile "D:\xxx\.dsh\profiles\desktop"`。

exe 不需要你手动安装：宿主半侧第一次加载时会把它铺到 `%LOCALAPPDATA%\DshEdgeDock\` 并拉起、之后每 8 秒守护一次。

### 卸载

```powershell
# 1. 关掉助手进程
Stop-Process -Name DshEdgeDock -ErrorAction SilentlyContinue
# 2. 删插件目录（两处）
Remove-Item "$env:USERPROFILE\.dsh\profiles\desktop\plugins\dsh-edge-dock" -Recurse -Force
Remove-Item "$env:USERPROFILE\.dsh\profiles\desktop\node_modules\dsh-edge-dock" -Recurse -Force
```

再删掉 profile `package.json` 里的 `"dsh-edge-dock"` 两处登记即可；`%LOCALAPPDATA%\DshEdgeDock\` 可以直接删（日志与配置）。

---

## 设置项

| 开关 | 作用 |
| --- | --- |
| 启用贴边自动收起 | 总开关，关掉后窗口行为完全复原 |
| 窗口置顶 | 让 Harness 始终在最前（收起时会自动临时撤掉，不会挡住别人的全屏） |
| 其他窗口全屏时也能唤出 | 前台有全屏程序时，鼠标碰到边缘也会把 Harness 提到最前 |
| Windows 窗口布局的窗口保持原样 | 用贴靠布局摆成半屏/三分屏的窗口不自动收起（默认开） |
| 开机自动启动 | 写 `HKCU\...\Run`，只对当前用户、当前机器有效 |

页面上还有「立即唤出 / 立即收起 / 重启助手」三个按钮，配合状态条（是否检测到窗口、当前贴哪边、是否已收起）方便排查。

---

## 结构

| 文件 | 作用 |
| --- | --- |
| `lib/index.js` | 宿主半侧：把 `bin/DshEdgeDock.exe` 铺到 `%LOCALAPPDATA%\DshEdgeDock\`，拉起并守护，暴露同源接口 `/api-dsh-edge-dock/state\|set\|action` |
| `lib/client.js` | 浏览器半侧：设置页那一屏（状态条 + 开关 + 按钮），2 秒轮询 |
| `bin/DshEdgeDock.exe` | 真正干活的 Win32 小程序：找 DSH 主窗口、判贴边、滑动收起/伸出、置顶、托盘菜单 |
| `src/EdgeDock.cs` | 上面那个 exe 的 C# 源码（单文件，WinForms + P/Invoke） |
| `src/build.ps1` | 用系统自带 csc 重新编译 exe |
| `cordis.patch.yml` | 把 `dsh-edge-dock` 挂成一条 loader 行 |
| `install.ps1` | 一键安装/迁移脚本 |

---

## 设计取舍

- **不改 app.asar**：DSH 夜间自动更新会把它覆盖，重打包 213MB 的 asar 还有 integrity 风险。所以窗口操作全部放在一个独立 exe 里，Harness 更新不影响它。
- **插件只当遥控器**：浏览器里拿不到窗口句柄，所以页面上所有开关都经宿主半侧转成一句 TCP 命令发给 exe（`127.0.0.1:59731`，一行命令一行应答，仅回环地址）。
- **贴靠布局豁免**：用 Windows「贴靠布局」摆成半屏/三分屏的窗口不自动收起，只有自己拖到边缘的才收（可在设置里关）。
- **全屏唤出**：唤出时如果前台窗口铺满整屏，就临时把 Harness 提到最前；点外面收起时自动让回，不会长期霸占最前。

## 行为细节

- 收起 = 滑到屏幕边缘只留 4px（相邻有显示器时改为隐藏），动画 6 步；唤出时鼠标只要碰到那条边的任意高度即可。
- 贴边判定留了 64px 容差：手动拖到边缘附近（稍许越界也算）就能识别。
- 窗口被自己拖离边缘、或把窗口最小化/收进托盘，都不会误判成「已收起」。
- 多显示器：贴的那侧如果还有屏幕，就不留 4px 而是直接隐藏窗口（避免遮住邻屏）。
- 托盘图标右键：暂停自动收起、窗口置顶、全屏唤出、开机自动启动、显示窗口、退出。
- 配置与日志在 `%LOCALAPPDATA%\DshEdgeDock\`（`config.ini`、`edgedock.log`、`selftest.txt`）。

## 自检

```powershell
& "$env:LOCALAPPDATA\DshEdgeDock\DshEdgeDock.exe" --selftest
```

会跑 8 组窗口矩形用例（半屏、三分屏、四分之一、整屏、右半屏底部等）并把结果写到 `%LOCALAPPDATA%\DshEdgeDock\selftest.txt`。

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\src\build.ps1
```

用的是 `C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（Windows 自带，C# 5 语法）。

## 已知限制

- 只支持 Windows；非 win32 平台宿主半侧会直接返回，不加载任何东西
- exe 未签名，首次运行可能被 SmartScreen / 杀软提示
- 端口固定 `59731` + 单实例互斥，同一台机器不会跑两份
- Windows 的「贴靠布局」窗口按几何形状识别（半屏/三分屏/四分之一/整屏），非标准尺寸的自定义布局仍会被当成贴边

## License

MIT，见 [LICENSE](LICENSE)。
