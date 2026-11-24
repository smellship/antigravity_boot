# Antigravity Boot Studio · 开机动画工坊

一个专为 **Windows 版 Antigravity** 打造的原生开机动画与动态氛围背景插件（Native Antigravity Plugin）。

> 灵感致敬 [wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations) —— 在其「零入侵外置浮层、不修改应用本体」的基础上，补齐 Windows 版 Antigravity 适配，并进一步升级为带侧边栏工坊、离线内置主题、多主题画廊与智能护眼节能的原生插件。

## 相比独立小工具多了什么？

| 能力 | 独立脚本版 (`ai-boot-animations`) | Antigravity Boot Studio 插件版 |
|---|---|---|
| **Windows Antigravity 适配** | 仅支持 macOS | **原生 Windows 10/11 零 SDK 编译与 Per-Monitor DPI 跟随** |
| **开箱即用** | 需下载 ~70MB 视频 | **内置纯 WPF 离线主题「✦ 星穹极光」，0 下载即可运行** |
| **控制入口** | 系统托盘菜单 + 独立弹窗 | **Antigravity 原生侧边栏 `Boot Studio` + 对话自然语言操控** |
| **多主题画廊** | 单一内置动画 | **支持一键获取官方 3 套同人素材包 + 本地视频/壁纸自定义 + 启动随机抽签** |
| **展示形态** | 全屏遮罩淡出 | **支持「开屏全屏 → 平滑缩进左下角动态挂件」、全程小窗、全屏背景三种形态自由切换（悬停播控 + 直接拖拽缩放，Antigravity 更新不失效）** |
| **智能守护** | 基础显隐跟随 | **编码专注自动变淡 + 省电模式/最小化自动停解码 + 窗口多开锁定** |
| **启动托管模式** | 仅常驻后台 | **支持「纯插件托管（零残留）」与「极速抢跑自启」自由切换** |

## 架构一览

```text
antigravity_boot/
├── plugin.json                # Antigravity 原生插件清单
├── assets/logo.svg            # 插件图标
├── rules/AGENTS.md            # 自然语言请求路由规则
├── skills/boot-animation/     # Agent 对话操控技能
├── engine/                    # 零 SDK C# 5.0 / WPF + Win32 透明浮层引擎
│   ├── Program.cs             # 入口、DPI 感知、双显卡高性能调度、单实例锁
│   ├── Native.cs              # Win32 P/Invoke、DWM 真实窗口边框、输入空闲与电源状态检测
│   ├── Models.cs              # 原子配置读写、主题注册表与日志轮转
│   ├── WindowFinder.cs        # Antigravity 主窗口探测与多开锁定
│   ├── ProceduralStage.cs     # 内置离线「星穹极光」矢量粒子渲染器
│   ├── OverlayStage.cs        # 视频双缓冲无缝接缝 / 图片微呼吸 / 字幕 / 专注变淡 / 省电暂停
│   ├── ControlServer.cs       # 127.0.0.1 环回 HTTP + Bearer Token 实时控制服务
│   └── TrayApp.cs             # 生命周期托管、自检模式与可选系统托盘
├── sidecars/studio/           # Antigravity Auxiliary Pane 侧边栏控制台 (Node.js)
│   ├── sidecar.json
│   ├── main.mjs               # 引擎守护进程、官方素材流式下载器、Windows 文件选择器桥
│   └── index.html / styles.css / app.js
└── scripts/
    ├── build_engine.ps1       # 使用 Windows 自带 csc.exe 编译引擎（无需装 .NET SDK）
    ├── studio_core.mjs        # CLI 与 Sidecar 共用配置/主题/下载核心
    ├── boot_ctl.mjs           # 对话/命令行控制工具
    ├── install.ps1            # 一键编译并安装到 ~/.gemini/config/plugins/antigravity-boot
    └── uninstall.ps1          # 完整卸载清理
```

## 快速安装

需要 **Windows 10 / 11**（已内置 .NET Framework 4.x 与 PowerShell，无需安装额外 SDK）：

```powershell
git clone https://github.com/smellship/antigravity_boot.git
cd antigravity_boot
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

安装完成后，在 Antigravity 的 **Customizations / Plugins** 页面启用 `antigravity-boot`（含 `Boot Studio` 侧边栏），打开侧边栏即可使用。

## 四大面板与功能

1. **播控台**：支持 **左下角挂件（开屏全屏 → 平滑缩进 Settings 上方角落）/ 全程小窗 / 全屏背景** 三种形态一键切换；悬停挂件即可重播、`↺` 一键复位回 Settings 上方初始位置、切换形态或主题，并支持直接拖拽位置与右下角默认锁定 `16:9` 等比缩放（基于 Win32 窗口坐标吸附，不修改客户端任何文件，更新软件永不失效）；滑块调节清晰度/透明度与音量毫秒级实时生效。
2. **主题画廊**：
   - 默认内置离线矢量主题 **✦ 星穹极光 (Stellar Aurora)**；
   - 支持一键拉取官方素材库：**✦ Gemini 双剑少女**、**🌙 星空塔罗龙娘**、**✨ 月下持剑少女**；
   - 支持 **＋ 创建本地主题**：直接通过系统文件对话框选择本地 `.mp4/.wmv` 视频或 `.png/.jpg/.webp` 壁纸（自动叠加微呼吸动态缩放），并自定义淡出时间点。
3. **智能守护**：
   - **极轻量化低功耗引擎**：单管线循环彻底释放开屏解码资源，并内置 Windows 原生 WinRT `MediaTranscoder` 自动生成 `640×360` H.264 挂件轻量缓存（体积压缩约 95%）；
   - **锁屏 / 息屏 / 失焦自动暂停**：最小化、锁屏、息屏、开启省电模式，或切换去其他窗口 3 秒后自动暂停解码与渲染（切回或悬停挂件秒级恢复）；
   - **编码专注自动变淡**：全屏背景下打字操作时平滑压低浓度，停下几秒后自动恢复氛围；
   - **启动模式切换**：默认纯插件托管（随 Antigravity 启停、不留后台），也可一键开启「极速抢跑开机自启」。
4. **诊断日志**：实时查看浮层 PID、环回端口、内存占用、循环圈数、自愈次数与运行日志。

## 命令行与对话操控

安装后可以直接在对话中对 Antigravity 说：

- 「打开 Boot Studio 控制台，帮我播放一次开机动画」
- 「下载 Gemini 双剑少女素材包并设为当前主题」
- 「把循环背景透明度调到 18%，开启专注自动变淡」

或者在终端调用 CLI：

```powershell
node "$env:USERPROFILE\.gemini\config\plugins\antigravity-boot\scripts\boot_ctl.mjs" status
node "$env:USERPROFILE\.gemini\config\plugins\antigravity-boot\scripts\boot_ctl.mjs" packs download gemini-twinblades
node "$env:USERPROFILE\.gemini\config\plugins\antigravity-boot\scripts\boot_ctl.mjs" config --set residualAlpha=0.18
```

## 卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

如需连同下载的素材包和个人配置一并清空，可追加 `-PurgeData`。

## 开源协议与致谢

- 本插件代码采用 **[MIT License](LICENSE)** 开源。
- 可选在线下载的官方同人动画素材包来自 **[wynneceles/ai-boot-animations](https://github.com/wynneceles/ai-boot-animations)**，本仓库**不包含**任何第三方视频/图片素材二进制文件；按需下载后，这些素材仍受原作者声明的 **CC BY-NC-ND 4.0**（及对应字体/音效开源协议）约束，仅限个人安装体验，请勿商用或二次修改分发。
