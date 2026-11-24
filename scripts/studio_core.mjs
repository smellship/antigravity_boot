import { execFileSync } from 'node:child_process';
import {
  copyFileSync,
  createWriteStream,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { homedir } from 'node:os';
import { basename, dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const TRANSCODE_SCRIPT = join(dirname(fileURLToPath(import.meta.url)), 'transcode_widget.ps1');

export const DEFAULT_CONFIG = {
  activeThemeId: 'stellar-aurora',
  shuffleOnBoot: false,
  playIntro: true,
  loopBackground: true,
  residualAlpha: 0.24,
  introVolume: 0.9,
  stretchMode: 'UniformToFill',
  focusDimEnabled: true,
  focusDimFactor: 0.45,
  focusIdleSeconds: 4.0,
  pauseOnBatterySaver: true,
  showTrayIcon: false,
  targetProcesses: ['Antigravity'],
  lockedWindowHwnd: 0,
  displayMode: 'widget',
  widgetAnchor: 'bottom-left',
  widgetOffsetX: 12,
  widgetOffsetY: 52,
  widgetWidth: 234,
  widgetHeight: 132,
  widgetCornerRadius: 12,
  widgetOpacity: 0.94,
  widgetClickThrough: false,
  widgetLockAspectRatio: true,
  lowPowerLoop: true,
  pauseWhenUnfocused: true,
  unfocusPauseDelaySeconds: 3.0,
};

export const BUILTIN_THEMES = [
  {
    id: 'stellar-aurora',
    name: '✦ 星穹极光 · Stellar Aurora',
    description: '内置离线矢量粒子主题：双弧星芒开屏 + 透出后星尘与极光流线呼吸律动，零外链依赖。',
    kind: 'procedural',
    builtin: true,
    introPath: '',
    loopPath: '',
    imagePath: '',
    previewPath: '',
    fontPath: '',
    fadeStartSeconds: 4.2,
    fadeDurationSeconds: 1.2,
    showSubtitles: true,
    subtitleFadeSeconds: 0.5,
    subtitleWidthRatio: 0.56,
    accentColor: '#60A5FA',
    subtitles: [
      { text: 'Twin stars aligned · Ready to build.', start: 1.6, end: 4.1 },
    ],
  },
];

export const OFFICIAL_PACKS = [
  {
    id: 'gemini-twinblades',
    name: '✦ Gemini 双剑少女 (Antigravity 专属)',
    subtitle: '发光线稿 → 化为实体 → 剑尖星芒 → 双剑相击',
    description: '源自 wynneceles/ai-boot-animations 的 Antigravity 原生双剑少女主题，含音效、英文配音与内置字幕。',
    author: 'wynneceles / ai-boot-animations',
    license: 'CC BY-NC-ND 4.0（可个人安装使用，不可商用或改后再发布）',
    repoUrl: 'https://github.com/wynneceles/ai-boot-animations/tree/main/antigravity',
    sizeHint: '约 71 MB',
    accentColor: '#60A5FA',
    fadeStartSeconds: 12.93,
    fadeDurationSeconds: 1.2,
    showSubtitles: false,
    subtitleFadeSeconds: 0.5,
    subtitleWidthRatio: 0.6,
    subtitles: [
      { text: 'Where twin stars align, my blades carve a new horizon.', start: 2.68, end: 6.86 },
      { text: 'Ready to rewrite the universe?', start: 8.0, end: 10.2 },
    ],
    files: {
      'intro.mp4': 'antigravity/assets/intro.mp4',
      'loop.mp4': 'antigravity/assets/loop.mp4',
      'preview.jpg': 'antigravity/docs/preview.jpg',
      'LICENSE.md': 'antigravity/assets/LICENSE.md',
    },
  },
  {
    id: 'codex-tarot',
    name: '🌙 星空塔罗龙娘 (Codex / GPT)',
    subtitle: '发光线稿 → 展翅、七张牌亮起 → 甩牌挡嘴',
    description: '带音效、英文配音与 Libre Bodoni 优雅花体动态字幕的星空塔罗主题。',
    author: 'wynneceles / ai-boot-animations',
    license: 'CC BY-NC-ND 4.0（可个人安装使用，不可商用或改后再发布）',
    repoUrl: 'https://github.com/wynneceles/ai-boot-animations/tree/main/codex',
    sizeHint: '约 56 MB',
    accentColor: '#A78BFA',
    fadeStartSeconds: 11.4,
    fadeDurationSeconds: 1.2,
    showSubtitles: true,
    subtitleFadeSeconds: 0.5,
    subtitleWidthRatio: 0.6,
    subtitles: [
      { text: 'Every answer... is already in my hand.', start: 3.2, end: 7.2 },
      { text: 'All I lack... is your question.', start: 8.65, end: 11.6 },
    ],
    files: {
      'intro.mp4': 'codex/assets/intro.mp4',
      'loop.mp4': 'codex/assets/loop.mp4',
      'preview.jpg': 'codex/docs/preview.jpg',
      'LibreBodoni-Italic.ttf': 'codex/windows/assets/LibreBodoni-Italic.ttf',
      'LICENSE.md': 'codex/assets/LICENSE.md',
    },
  },
  {
    id: 'claude-moonblade',
    name: '✨ 月下持剑少女 (Claude)',
    subtitle: '月下线稿 → 上色 → 剑光、火凤、烟花',
    description: '经典月下持剑少女主题，含音效与动态英文字幕。',
    author: 'wynneceles / ai-boot-animations',
    license: 'CC BY-NC-ND 4.0（可个人安装使用，不可商用或改后再发布）',
    repoUrl: 'https://github.com/wynneceles/ai-boot-animations/tree/main/claude',
    sizeHint: '约 53 MB',
    accentColor: '#F59E0B',
    fadeStartSeconds: 10.767,
    fadeDurationSeconds: 1.2,
    showSubtitles: true,
    subtitleFadeSeconds: 0.5,
    subtitleWidthRatio: 0.6,
    subtitles: [
      { text: 'Under the moonlight... the blade awakens.', start: 4.087, end: 7.189 },
      { text: 'I am Claude. Ask me anything.', start: 8.281, end: 11.3 },
    ],
    files: {
      'intro.mp4': 'claude/assets/intro.mp4',
      'loop.mp4': 'claude/assets/loop.mp4',
      'preview.jpg': 'claude/docs/preview.jpg',
      'LibreBodoni-Italic.ttf': 'claude/windows/assets/LibreBodoni-Italic.ttf',
      'LICENSE.md': 'claude/assets/LICENSE.md',
    },
  },
];

export function resolveDataDir(overrideDir) {
  if (overrideDir) return resolve(overrideDir);
  if (process.env.ANTIGRAVITY_BOOT_DATA_DIR) return resolve(process.env.ANTIGRAVITY_BOOT_DATA_DIR);
  const localApp = process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local');
  return join(localApp, 'AntigravityBootStudio');
}

export function pathsFor(dataDir) {
  const dir = resolveDataDir(dataDir);
  return {
    dataDir: dir,
    configPath: join(dir, 'config.json'),
    themesPath: join(dir, 'themes.json'),
    packsDir: join(dir, 'packs'),
    customDir: join(dir, 'custom'),
    runtimePath: join(dir, 'runtime.json'),
    logPath: join(dir, 'overlay.log'),
  };
}

export function readJsonSafe(filePath, fallback = null) {
  try {
    if (existsSync(filePath)) {
      return JSON.parse(readFileSync(filePath, 'utf-8').replace(/^\uFEFF/, ''));
    }
  } catch {
    // ignore parse error and use fallback
  }
  return fallback;
}

export function atomicWriteJson(filePath, obj) {
  const dir = join(filePath, '..');
  mkdirSync(dir, { recursive: true });
  const tmp = `${filePath}.${process.pid}.${Math.random().toString(36).slice(2, 8)}.tmp`;
  writeFileSync(tmp, `${JSON.stringify(obj, null, 2)}\n`, 'utf-8');
  renameSync(tmp, filePath);
}

export function clampConfig(raw) {
  const cfg = { ...DEFAULT_CONFIG, ...(raw || {}) };
  cfg.activeThemeId = String(cfg.activeThemeId || 'stellar-aurora');
  cfg.shuffleOnBoot = Boolean(cfg.shuffleOnBoot);
  cfg.playIntro = Boolean(cfg.playIntro);
  cfg.loopBackground = Boolean(cfg.loopBackground);
  cfg.residualAlpha = Math.max(0, Math.min(0.8, Number(cfg.residualAlpha ?? 0.24)));
  cfg.introVolume = Math.max(0, Math.min(1, Number(cfg.introVolume ?? 0.9)));
  cfg.stretchMode = cfg.stretchMode === 'Uniform' ? 'Uniform' : 'UniformToFill';
  cfg.focusDimEnabled = Boolean(cfg.focusDimEnabled);
  cfg.focusDimFactor = Math.max(0.1, Math.min(1, Number(cfg.focusDimFactor ?? 0.45)));
  cfg.focusIdleSeconds = Math.max(1, Math.min(30, Number(cfg.focusIdleSeconds ?? 4.0)));
  cfg.pauseOnBatterySaver = Boolean(cfg.pauseOnBatterySaver);
  cfg.showTrayIcon = Boolean(cfg.showTrayIcon);
  cfg.targetProcesses = Array.isArray(cfg.targetProcesses) && cfg.targetProcesses.length > 0
    ? cfg.targetProcesses.map((x) => String(x).trim()).filter(Boolean)
    : ['Antigravity'];
  cfg.lockedWindowHwnd = Number(cfg.lockedWindowHwnd || 0);
  cfg.displayMode = ['widget', 'widget-always', 'fullscreen'].includes(cfg.displayMode) ? cfg.displayMode : 'widget';
  cfg.widgetAnchor = ['bottom-left', 'bottom-right', 'top-left', 'top-right'].includes(cfg.widgetAnchor) ? cfg.widgetAnchor : 'bottom-left';
  cfg.widgetOffsetX = Math.max(0, Math.min(1600, Number(cfg.widgetOffsetX ?? 12)));
  cfg.widgetOffsetY = Math.max(0, Math.min(1200, Number(cfg.widgetOffsetY ?? 52)));
  cfg.widgetWidth = Math.max(140, Math.min(720, Number(cfg.widgetWidth ?? 234)));
  cfg.widgetLockAspectRatio = cfg.widgetLockAspectRatio !== false;
  if (cfg.widgetLockAspectRatio) {
    cfg.widgetHeight = Math.round((cfg.widgetWidth * 9) / 16);
  }
  cfg.widgetHeight = Math.max(78, Math.min(480, Number(cfg.widgetHeight ?? 132)));
  cfg.widgetCornerRadius = Math.max(0, Math.min(32, Number(cfg.widgetCornerRadius ?? 12)));
  cfg.widgetOpacity = Math.max(0.2, Math.min(1, Number(cfg.widgetOpacity ?? 0.94)));
  cfg.widgetClickThrough = Boolean(cfg.widgetClickThrough);
  cfg.lowPowerLoop = cfg.lowPowerLoop !== false;
  cfg.pauseWhenUnfocused = cfg.pauseWhenUnfocused !== false;
  cfg.unfocusPauseDelaySeconds = Math.max(0.5, Math.min(30, Number(cfg.unfocusPauseDelaySeconds ?? 3.0)));
  return cfg;
}

export function loadConfig(dataDir) {
  const { configPath } = pathsFor(dataDir);
  const raw = readJsonSafe(configPath, null);
  const cfg = clampConfig(raw);
  if (!raw) atomicWriteJson(configPath, cfg);
  return cfg;
}

export function saveConfig(dataDir, cfg) {
  const { configPath } = pathsFor(dataDir);
  const clean = clampConfig(cfg);
  atomicWriteJson(configPath, clean);
  return clean;
}

export function patchConfig(dataDir, patch) {
  const current = loadConfig(dataDir);
  const next = { ...current, ...(patch || {}) };
  if (next.widgetLockAspectRatio !== false && patch?.widgetHeight !== undefined && patch?.widgetWidth === undefined) {
    next.widgetWidth = Math.round((Number(patch.widgetHeight) * 16) / 9);
  }
  return saveConfig(dataDir, next);
}

export function loadThemes(dataDir) {
  const { themesPath } = pathsFor(dataDir);
  const raw = readJsonSafe(themesPath, null);
  const list = Array.isArray(raw?.themes) ? [...raw.themes] : [];
  const hasBuiltin = list.some((t) => t && t.id === 'stellar-aurora');
  if (!hasBuiltin) {
    list.unshift(structuredClone(BUILTIN_THEMES[0]));
  } else {
    for (let i = 0; i < list.length; i++) {
      if (list[i].id === 'stellar-aurora') list[i].builtin = true;
    }
  }
  if (!raw) saveThemes(dataDir, list);
  return list;
}

export function saveThemes(dataDir, themes) {
  const { themesPath } = pathsFor(dataDir);
  atomicWriteJson(themesPath, { themes });
  return themes;
}

export function upsertTheme(dataDir, theme) {
  const themes = loadThemes(dataDir);
  const idx = themes.findIndex((t) => t.id === theme.id);
  if (idx >= 0) themes[idx] = { ...themes[idx], ...theme };
  else themes.push(theme);
  saveThemes(dataDir, themes);
  return themes;
}

export function deleteTheme(dataDir, themeId) {
  if (!themeId || themeId === 'stellar-aurora') {
    throw new Error('内置离线主题不可删除');
  }
  const themes = loadThemes(dataDir);
  const target = themes.find((t) => t.id === themeId);
  if (!target) throw new Error(`未找到主题：${themeId}`);
  const next = themes.filter((t) => t.id !== themeId);
  saveThemes(dataDir, next);

  // Clean up managed pack/custom folder if inside our data dir
  const { packsDir, customDir } = pathsFor(dataDir);
  const packFolder = join(packsDir, themeId);
  if (existsSync(packFolder)) rmSync(packFolder, { recursive: true, force: true });
  const custFolder = join(customDir, themeId);
  if (existsSync(custFolder)) rmSync(custFolder, { recursive: true, force: true });

  const cfg = loadConfig(dataDir);
  if (cfg.activeThemeId === themeId) {
    patchConfig(dataDir, { activeThemeId: 'stellar-aurora' });
  }
  return next;
}

function sanitizeSlug(name) {
  const slug = String(name || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return (slug || 'custom') + '-' + Date.now().toString(36);
}

export function createCustomTheme(dataDir, opts) {
  const name = String(opts?.name || '').trim();
  if (!name) throw new Error('请输入自定义主题名称');

  const introSrc = String(opts?.introPath || '').trim();
  const loopSrc = String(opts?.loopPath || '').trim();
  const imageSrc = String(opts?.imagePath || '').trim();
  const copyAssets = opts?.copyAssets !== false;

  if (!introSrc && !loopSrc && !imageSrc) {
    throw new Error('请至少选择一个开屏视频、循环视频或静态/动态壁纸图片');
  }

  for (const [label, p] of [['开屏视频', introSrc], ['循环视频', loopSrc], ['壁纸图片', imageSrc]]) {
    if (p && !existsSync(p)) {
      throw new Error(`${label}路径不存在：${p}`);
    }
  }

  const id = opts?.id ? String(opts.id) : sanitizeSlug(name);
  const { customDir } = pathsFor(dataDir);
  const themeDir = join(customDir, id);
  mkdirSync(themeDir, { recursive: true });

  const stageFile = (src, prefix) => {
    if (!src) return '';
    if (!copyAssets) return resolve(src);
    const dest = join(themeDir, `${prefix}${extname(src) || ''}`);
    copyFileSync(src, dest);
    return dest;
  };

  const kind = imageSrc && !introSrc && !loopSrc ? 'image' : 'video';
  const theme = {
    id,
    name,
    description: String(opts?.description || (kind === 'image' ? '自定义本地图片氛围背景主题' : '自定义本地视频开屏主题')),
    kind,
    builtin: false,
    introPath: stageFile(introSrc, 'intro'),
    loopPath: stageFile(loopSrc, 'loop'),
    widgetLoopPath: '',
    imagePath: stageFile(imageSrc, 'wallpaper'),
    previewPath: kind === 'image' ? stageFile(imageSrc, 'preview') : '',
    fontPath: '',
    fadeStartSeconds: Number(opts?.fadeStartSeconds ?? (kind === 'image' ? 3.0 : 8.0)),
    fadeDurationSeconds: Number(opts?.fadeDurationSeconds ?? 1.2),
    showSubtitles: Boolean(opts?.showSubtitles && Array.isArray(opts?.subtitles) && opts.subtitles.length > 0),
    subtitleFadeSeconds: 0.5,
    subtitleWidthRatio: 0.6,
    accentColor: String(opts?.accentColor || '#38BDF8'),
    subtitles: Array.isArray(opts?.subtitles) ? opts.subtitles : [],
  };

  upsertTheme(dataDir, theme);
  if (theme.kind === 'video' && theme.loopPath) {
    try {
      const opt = ensureWidgetLightCache(dataDir, theme.id);
      if (opt?.ok) theme.widgetLoopPath = opt.outputPath;
    } catch { }
  }
  return theme;
}

function mirrorUrlsFor(relPath) {
  return [
    `https://raw.githubusercontent.com/wynneceles/ai-boot-animations/main/${relPath}`,
    `https://cdn.jsdelivr.net/gh/wynneceles/ai-boot-animations@main/${relPath}`,
  ];
}

async function downloadFileWithMirrors(relPath, destPath, onBytes) {
  const urls = mirrorUrlsFor(relPath);
  const tmpPath = `${destPath}.part`;
  let lastErr = null;

  for (const url of urls) {
    try {
      const res = await fetch(url, {
        headers: { 'User-Agent': 'AntigravityBootStudio/1.0' },
        redirect: 'follow',
      });
      if (!res.ok) throw new Error(`HTTP ${res.status} (${url})`);
      const total = Number(res.headers.get('content-length') || 0);
      const reader = res.body?.getReader();
      if (!reader) throw new Error('ReadableStream not supported');

      mkdirSync(join(destPath, '..'), { recursive: true });
      const ws = createWriteStream(tmpPath);
      let received = 0;

      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          received += value.byteLength;
          ws.write(Buffer.from(value));
          onBytes?.(received, total, url);
        }
        await new Promise((resolveDone, rejectDone) => {
          ws.on('finish', resolveDone);
          ws.on('error', rejectDone);
          ws.end();
        });
      } catch (err) {
        try { ws.destroy(); } catch {}
        throw err;
      }

      if (existsSync(destPath)) rmSync(destPath, { force: true });
      renameSync(tmpPath, destPath);
      return { url, bytes: received };
    } catch (err) {
      lastErr = err;
      try { if (existsSync(tmpPath)) rmSync(tmpPath, { force: true }); } catch {}
    }
  }
  throw lastErr || new Error(`下载失败：${relPath}`);
}

export async function downloadOfficialPack(dataDir, packId, onProgress) {
  const pack = OFFICIAL_PACKS.find((p) => p.id === packId);
  if (!pack) throw new Error(`未知官方素材包：${packId}`);

  const { packsDir } = pathsFor(dataDir);
  const destDir = join(packsDir, pack.id);
  mkdirSync(destDir, { recursive: true });

  const entries = Object.entries(pack.files);
  let idx = 0;
  for (const [localName, relPath] of entries) {
    idx++;
    const destFile = join(destDir, localName);
    onProgress?.({
      packId: pack.id,
      status: 'downloading',
      file: localName,
      fileIndex: idx,
      fileCount: entries.length,
      received: 0,
      total: 0,
      percent: Math.round(((idx - 1) / entries.length) * 100),
    });

    await downloadFileWithMirrors(relPath, destFile, (received, total) => {
      const fileFrac = total > 0 ? received / total : 0;
      const overall = Math.round((((idx - 1) + fileFrac) / entries.length) * 100);
      onProgress?.({
        packId: pack.id,
        status: 'downloading',
        file: localName,
        fileIndex: idx,
        fileCount: entries.length,
        received,
        total,
        percent: overall,
      });
    });
  }

  const theme = {
    id: pack.id,
    name: pack.name,
    description: pack.description,
    kind: 'video',
    builtin: false,
    introPath: join(destDir, 'intro.mp4'),
    loopPath: join(destDir, 'loop.mp4'),
    widgetLoopPath: '',
    imagePath: '',
    previewPath: join(destDir, 'preview.jpg'),
    fontPath: pack.files['LibreBodoni-Italic.ttf'] ? join(destDir, 'LibreBodoni-Italic.ttf') : '',
    fadeStartSeconds: pack.fadeStartSeconds,
    fadeDurationSeconds: pack.fadeDurationSeconds,
    showSubtitles: pack.showSubtitles,
    subtitleFadeSeconds: pack.subtitleFadeSeconds,
    subtitleWidthRatio: pack.subtitleWidthRatio,
    accentColor: pack.accentColor,
    subtitles: pack.subtitles,
  };

  upsertTheme(dataDir, theme);
  onProgress?.({
    packId: pack.id,
    status: 'downloading',
    file: '正在生成 640×360 轻量挂件缓存...',
    fileIndex: entries.length,
    fileCount: entries.length,
    received: 1,
    total: 1,
    percent: 98,
  });
  try {
    const opt = ensureWidgetLightCache(dataDir, theme.id);
    if (opt?.ok) theme.widgetLoopPath = opt.outputPath;
  } catch { }
  onProgress?.({
    packId: pack.id,
    status: 'done',
    file: '',
    fileIndex: entries.length,
    fileCount: entries.length,
    received: 1,
    total: 1,
    percent: 100,
  });
  return theme;
}

export function ensureWidgetLightCache(dataDir, themeId) {
  const themes = loadThemes(dataDir);
  const target = themes.find((t) => t.id === themeId);
  if (!target) throw new Error(`未找到主题：${themeId}`);
  if (target.kind !== 'video' || !target.loopPath || !existsSync(target.loopPath)) {
    return { ok: false, skipped: true, reason: '该主题没有循环视频文件' };
  }
  if (!existsSync(TRANSCODE_SCRIPT)) {
    throw new Error(`找不到转码脚本：${TRANSCODE_SCRIPT}`);
  }
  const outPath = join(dirname(target.loopPath), 'loop_widget.mp4');
  const raw = execFileSync(
    'powershell.exe',
    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', TRANSCODE_SCRIPT, '-InputPath', target.loopPath, '-OutputPath', outPath],
    { encoding: 'utf-8', timeout: 120000 }
  ).trim();
  const parsed = JSON.parse(raw);
  target.widgetLoopPath = outPath;
  upsertTheme(dataDir, target);
  return { ok: true, themeId: target.id, ...parsed };
}

export function isPackInstalled(dataDir, packId) {
  const { packsDir } = pathsFor(dataDir);
  const dir = join(packsDir, packId);
  return existsSync(join(dir, 'intro.mp4')) && existsSync(join(dir, 'loop.mp4'));
}

export function readRuntime(dataDir) {
  const { runtimePath } = pathsFor(dataDir);
  return readJsonSafe(runtimePath, null);
}

export async function callOverlay(dataDir, method, routePath, body = null, timeoutMs = 2500) {
  const rt = readRuntime(dataDir);
  if (!rt?.port || !rt?.token) return null;

  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  try {
    const headers = { Authorization: `Bearer ${rt.token}` };
    const opts = { method, headers, signal: ctrl.signal };
    if (body !== null && method !== 'GET') {
      headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    const res = await fetch(`http://127.0.0.1:${rt.port}${routePath}`, opts);
    const txt = await res.text();
    try { return JSON.parse(txt); }
    catch { return { ok: res.ok, raw: txt }; }
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

export async function isOverlayAlive(dataDir) {
  const rt = readRuntime(dataDir);
  if (!rt?.port) return false;
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 1200);
  try {
    const res = await fetch(`http://127.0.0.1:${rt.port}/health`, { signal: ctrl.signal });
    if (!res.ok) return false;
    const data = await res.json();
    return Boolean(data?.ok);
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

export function readLogsTail(dataDir, maxLines = 60) {
  const { logPath } = pathsFor(dataDir);
  if (!existsSync(logPath)) return [];
  try {
    const lines = readFileSync(logPath, 'utf-8').split(/\r?\n/).filter(Boolean);
    return lines.slice(-maxLines);
  } catch {
    return [];
  }
}

const RUN_REG_PATH = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run';
const RUN_REG_NAME = 'AntigravityBootStudio';

export function getAutostartStatus() {
  try {
    const cmd = `$v = (Get-ItemProperty -Path '${RUN_REG_PATH}' -Name '${RUN_REG_NAME}' -ErrorAction SilentlyContinue).'${RUN_REG_NAME}'; if ($v) { Write-Output $v }`;
    const out = execFileSync('powershell.exe', ['-NoProfile', '-Command', cmd], { encoding: 'utf-8' }).trim();
    return { enabled: Boolean(out), command: out };
  } catch {
    return { enabled: false, command: '' };
  }
}

export function setAutostart(enabled, exePath, dataDir) {
  if (enabled) {
    if (!existsSync(exePath)) throw new Error(`找不到引擎程序：${exePath}`);
    const val = `"${resolve(exePath)}" --autostart --data-dir "${resolveDataDir(dataDir)}"`;
    const psVal = val.replace(/'/g, "''");
    const cmd = `New-Item -Path '${RUN_REG_PATH}' -Force | Out-Null; Set-ItemProperty -Path '${RUN_REG_PATH}' -Name '${RUN_REG_NAME}' -Value '${psVal}'`;
    execFileSync('powershell.exe', ['-NoProfile', '-Command', cmd], { encoding: 'utf-8' });
  } else {
    const cmd = `Remove-ItemProperty -Path '${RUN_REG_PATH}' -Name '${RUN_REG_NAME}' -ErrorAction SilentlyContinue`;
    execFileSync('powershell.exe', ['-NoProfile', '-Command', cmd], { encoding: 'utf-8' });
  }
  return getAutostartStatus();
}
