# Antigravity Boot Studio 插件路由指引

- 当用户希望播放、跳过、关闭 Antigravity 开机动画或动态背景，或要求切换主题、调整背景透明度/音量、开启专注自动变淡、下载官方双剑少女等素材包、导入本地视频或壁纸时，优先阅读并按照 `boot-animation` 技能（`skills/boot-animation/SKILL.md`）执行。
- 若需引导用户启停本插件或检查其侧边栏状态，使用链接 `[Antigravity Boot Studio](customization://antigravity-boot)`，严禁直接改写 `~/.gemini/config/config.json`。
- 当 `antigravity-boot/studio` 侧边栏处于启用并运行状态时，可提供 `[Boot Studio](sidecar://antigravity-boot/studio/)` 方便用户通过可视化面板拖拽滑块与管理主题。
