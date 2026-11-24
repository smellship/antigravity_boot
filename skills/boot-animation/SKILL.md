---
name: boot-animation
description: 控制 Antigravity Windows 开机动画与动态氛围背景（Antigravity Boot Studio）：播放或跳过开屏动画、切换内置星穹极光/官方双剑少女/星空塔罗等主题、实时调整透明度与音量、开启专注自动变淡或开机自启、添加本地视频或壁纸自定义主题。当用户提到开机动画、启动动画、动态背景、换主题、双剑少女、Boot Studio、背景透明度太高/太低时使用。
---

# Antigravity Boot Studio · 开机动画控制指南

本技能用于通过对话控制 **Antigravity Boot Studio** 插件的 Windows 浮层引擎，并引导用户在侧边栏控制台操作。

## 1. CLI 控制入口

所有底层配置、素材下载与播控均由插件自带的零依赖 Node 脚本完成：

```powershell
node "$env:USERPROFILE\.gemini\config\plugins\antigravity-boot\scripts\boot_ctl.mjs" <command> [args]
```

> 若从仓库目录直接调试，也可使用 `scripts/boot_ctl.mjs`。

### 常用指令速查

| 意图 | 执行命令 |
|---|---|
| 查看引擎状态、窗口与当前配置 | `node ".../boot_ctl.mjs" status` |
| 立即播放一次开屏动画 | `node ".../boot_ctl.mjs" play --mode intro` |
| 仅开启半透明循环动态背景 | `node ".../boot_ctl.mjs" play --mode loop` |
| 跳过当前开屏 / 淡出关闭浮层 | `node ".../boot_ctl.mjs" skip` 或 `stop` |
| 实时调透明度 / 音量 / 专注变淡 | `node ".../boot_ctl.mjs" config --set residualAlpha=0.18 --set focusDimEnabled=true` |
| 列出 / 切换本地主题 | `node ".../boot_ctl.mjs" themes list` / `themes select <id>` / `themes next` |
| 挂件归位左下角 (Settings 上方) | `node ".../boot_ctl.mjs" widget reset` |
| 列出 / 下载官方同人素材包 | `node ".../boot_ctl.mjs" packs list` / `packs download gemini-twinblades` |
| 添加自定义本地视频或壁纸主题 | `node ".../boot_ctl.mjs" custom add --name "<名称>" --intro "<路径>" --loop "<路径>"`（或 `--image "<路径>"`） |
| 开关 Windows 极速抢跑开机自启 | `node ".../boot_ctl.mjs" autostart on` / `off` / `status` |

## 2. 常见可配置项（`config --set`）

- `displayMode`: `widget`（开屏全屏后平滑缩进左下角挂件，默认）/ `widget-always`（全程小窗）/ `fullscreen`（全屏半透明背景）
- `widgetAnchor`: `bottom-left`（默认，贴在左下角 Settings 上方）/ `bottom-right` / `top-left` / `top-right`
- `widgetOffsetX` / `widgetOffsetY` / `widgetWidth` / `widgetHeight` / `widgetCornerRadius`: 挂件偏移与尺寸圆角（单位 DIP，默认 `12, 52, 234, 132, 12`）
- `widgetLockAspectRatio`: `true` / `false`（默认 `true`，拖动或调尺寸时锁定 16:9 比例）
- `widgetOpacity`: `0.2` ~ `1.0`（挂件清晰度，默认 `0.94`）
- `widgetClickThrough`: `true` / `false`（关闭时可鼠标悬停快捷播控 & 直接拖动位置/右下角调大小）
- `playIntro`: `true` / `false`（启动 Antigravity 时是否播开屏）
- `loopBackground`: `true` / `false`（透出后是否保留循环背景/挂件）
- `shuffleOnBoot`: `true` / `false`（每次启动在已安装主题间随机轮换）
- `residualAlpha`: `0.0` ~ `0.8`（全屏循环背景不透明度，推荐 `0.15` ~ `0.28`）
- `introVolume`: `0.0` ~ `1.0`（开屏音量）
- `stretchMode`: `UniformToFill`（铺满裁切）或 `Uniform`（完整居中）
- `focusDimEnabled`: `true` / `false`（专心输入代码时自动调淡）
- `focusDimFactor`: `0.1` ~ `1.0`（专注变淡保留比例）
- `focusIdleSeconds`: `1.0` ~ `30.0`（停下输入多少秒后平滑回亮）
- `pauseOnBatterySaver`: `true` / `false`（系统省电模式自动暂停解码）
- `showTrayIcon`: `true` / `false`（是否显示系统托盘图标）

## 3. 官方可选素材包 ID（需用户主动要求下载时拉取）

- `gemini-twinblades`: ✦ Gemini 双剑少女（Antigravity 专属，约 71 MB）
- `codex-tarot`: 🌙 星空塔罗龙娘（约 56 MB）
- `claude-moonblade`: ✨ 月下持剑少女（约 53 MB）

> 注意：上述官方同人视频素材源自 `wynneceles/ai-boot-animations`，授权为 **CC BY-NC-ND 4.0**（仅限个人安装分享，不可商用或改后再发布）。

## 4. 界面引导与约束

1. **不要手动修改 `~/.gemini/config/config.json`**。如果插件或 `Boot Studio` 侧边栏尚未启用，请给出链接 `[Antigravity Boot Studio](customization://antigravity-boot)` 引导用户在界面中开启。
2. 当 `antigravity-boot/studio` Sidecar 已启用并运行时，可以在回复中附带 `[Boot Studio](sidecar://antigravity-boot/studio/)` 一键打开可视化控制台。
3. 若执行 `play` 时返回引擎未启动（例如沙盒环境或 Sidecar 尚未开启），提示用户先启用并打开 `[Boot Studio](sidecar://antigravity-boot/studio/)` 或开启开机自启。
