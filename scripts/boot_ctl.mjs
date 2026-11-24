#!/usr/bin/env node
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  OFFICIAL_PACKS,
  callOverlay,
  createCustomTheme,
  deleteTheme,
  downloadOfficialPack,
  ensureWidgetLightCache,
  getAutostartStatus,
  isOverlayAlive,
  isPackInstalled,
  loadConfig,
  loadThemes,
  patchConfig,
  pathsFor,
  readLogsTail,
  resolveDataDir,
  setAutostart,
} from './studio_core.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = join(__dirname, '..');
const OVERLAY_EXE = join(PLUGIN_ROOT, 'bin', 'AntigravityBootOverlay.exe');

function parseArgs(argv) {
  const positional = [];
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const tok = argv[i];
    if (tok.startsWith('--')) {
      const key = tok.slice(2);
      const next = argv[i + 1];
      if (next !== undefined && !next.startsWith('--')) {
        if (key === 'set') {
          flags.set = flags.set || [];
          flags.set.push(next);
        } else {
          flags[key] = next;
        }
        i++;
      } else {
        flags[key] = true;
      }
    } else {
      positional.push(tok);
    }
  }
  return { positional, flags };
}

function parseTypedValue(raw) {
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  if (raw !== '' && !Number.isNaN(Number(raw))) return Number(raw);
  return raw;
}

function printJson(obj) {
  console.log(JSON.stringify(obj, null, 2));
}

async function main() {
  const { positional, flags } = parseArgs(process.argv.slice(2));
  const cmd = positional[0] || 'status';
  const sub = positional[1] || '';
  const dataDir = resolveDataDir(typeof flags['data-dir'] === 'string' ? flags['data-dir'] : '');

  if (cmd === 'help' || flags.help) {
    console.log(`Antigravity Boot Studio CLI
用法:
  node scripts/boot_ctl.mjs status [--data-dir <dir>]
  node scripts/boot_ctl.mjs play --mode intro|loop [--theme <id>] [--hwnd <hwnd>]
  node scripts/boot_ctl.mjs skip
  node scripts/boot_ctl.mjs stop
  node scripts/boot_ctl.mjs config --set key=value [--set key2=value2]
  node scripts/boot_ctl.mjs themes list|select <id>|next|optimize [<id>]|delete <id>
  node scripts/boot_ctl.mjs widget reset
  node scripts/boot_ctl.mjs packs list|download <packId>
  node scripts/boot_ctl.mjs custom add --name <name> [--intro <path>] [--loop <path>] [--image <path>] [--fade-start <sec>]
  node scripts/boot_ctl.mjs autostart status|on|off
  node scripts/boot_ctl.mjs logs`);
    return;
  }

  if (cmd === 'status') {
    const alive = await isOverlayAlive(dataDir);
    const live = alive ? await callOverlay(dataDir, 'GET', '/status') : null;
    printJson({
      dataDir,
      exeBuilt: existsSync(OVERLAY_EXE),
      engineAlive: alive,
      autostart: getAutostartStatus(),
      config: loadConfig(dataDir),
      stage: live?.stage || null,
      windows: live?.windows || [],
      themesCount: loadThemes(dataDir).length,
    });
    return;
  }

  if (cmd === 'play') {
    const mode = flags.mode === 'loop' ? 'loop' : 'intro';
    const themeId = typeof flags.theme === 'string' ? flags.theme : null;
    const hwnd = flags.hwnd ? Number(flags.hwnd) : 0;
    const res = await callOverlay(dataDir, 'POST', '/play', { mode, themeId, hwnd });
    if (!res) {
      printJson({
        ok: false,
        engineAlive: false,
        hint: 'Overlay 引擎尚未启动。请先在 Antigravity 打开侧边栏 [Boot Studio](sidecar://antigravity-boot/studio/) 或启用开机自启。',
      });
      process.exitCode = 1;
      return;
    }
    printJson(res);
    return;
  }

  if (cmd === 'skip') {
    const res = await callOverlay(dataDir, 'POST', '/skip', {});
    printJson(res || { ok: false, error: '引擎未运行' });
    return;
  }

  if (cmd === 'stop') {
    const res = await callOverlay(dataDir, 'POST', '/stop', { fast: true });
    printJson(res || { ok: false, error: '引擎未运行' });
    return;
  }

  if (cmd === 'config') {
    const entries = Array.isArray(flags.set) ? flags.set : [];
    if (entries.length === 0) {
      printJson(loadConfig(dataDir));
      return;
    }
    const patch = {};
    for (const pair of entries) {
      const eq = pair.indexOf('=');
      if (eq <= 0) throw new Error(`无效配置项（应为 key=value）：${pair}`);
      const k = pair.slice(0, eq).trim();
      const v = parseTypedValue(pair.slice(eq + 1).trim());
      patch[k] = v;
    }
    const updated = patchConfig(dataDir, patch);
    const live = await callOverlay(dataDir, 'POST', '/apply-live', patch);
    printJson({ ok: true, config: updated, appliedLive: Boolean(live) });
    return;
  }

  if (cmd === 'themes') {
    if (sub === 'list' || !sub) {
      const cfg = loadConfig(dataDir);
      const themes = loadThemes(dataDir);
      printJson({ activeThemeId: cfg.activeThemeId, themes });
      return;
    }
    if (sub === 'select') {
      const id = positional[2];
      if (!id) throw new Error('请指定主题 ID');
      const themes = loadThemes(dataDir);
      const found = themes.find((t) => t.id === id);
      if (!found) throw new Error(`未找到主题：${id}`);
      const cfg = patchConfig(dataDir, { activeThemeId: id });
      const live = await callOverlay(dataDir, 'POST', '/apply-live', { activeThemeId: id });
      printJson({ ok: true, activeThemeId: cfg.activeThemeId, theme: found, appliedLive: Boolean(live) });
      return;
    }
    if (sub === 'next') {
      const live = await callOverlay(dataDir, 'POST', '/next-theme', {});
      if (live) {
        printJson(live);
        return;
      }
      const cfg = loadConfig(dataDir);
      const themes = loadThemes(dataDir);
      const idx = themes.findIndex((t) => t.id === cfg.activeThemeId);
      const next = themes[(idx + 1) % themes.length];
      patchConfig(dataDir, { activeThemeId: next.id });
      printJson({ ok: true, activeThemeId: next.id, theme: next, appliedLive: false });
      return;
    }
    if (sub === 'optimize') {
      const cfg = loadConfig(dataDir);
      const id = positional[2] || cfg.activeThemeId;
      const res = ensureWidgetLightCache(dataDir, id);
      await callOverlay(dataDir, 'POST', '/reload', {});
      printJson(res);
      return;
    }
    if (sub === 'delete') {
      const id = positional[2];
      const next = deleteTheme(dataDir, id);
      await callOverlay(dataDir, 'POST', '/reload', {});
      printJson({ ok: true, remaining: next.length });
      return;
    }
    throw new Error(`未知 themes 子命令：${sub}`);
  }

  if (cmd === 'widget') {
    if (sub === 'reset') {
      const live = await callOverlay(dataDir, 'POST', '/reset-widget', {});
      const cfg = patchConfig(dataDir, {
        displayMode: 'widget',
        widgetAnchor: 'bottom-left',
        widgetOffsetX: 12,
        widgetOffsetY: 52,
        widgetWidth: 234,
        widgetHeight: 132,
        widgetCornerRadius: 12,
      });
      printJson({ ok: true, config: cfg, appliedLive: Boolean(live) });
      return;
    }
    throw new Error(`未知 widget 子命令：${sub}`);
  }

  if (cmd === 'packs') {
    if (sub === 'list' || !sub) {
      printJson(OFFICIAL_PACKS.map((p) => ({
        id: p.id,
        name: p.name,
        sizeHint: p.sizeHint,
        license: p.license,
        installed: isPackInstalled(dataDir, p.id),
      })));
      return;
    }
    if (sub === 'download') {
      const packId = positional[2] || 'gemini-twinblades';
      let lastPct = -1;
      const theme = await downloadOfficialPack(dataDir, packId, (ev) => {
        if (ev.percent !== lastPct && (ev.percent % 10 === 0 || ev.status === 'done')) {
          lastPct = ev.percent;
          console.error(`[${ev.packId}] ${ev.percent}% (${ev.file || '完成'})`);
        }
      });
      await callOverlay(dataDir, 'POST', '/reload', {});
      printJson({ ok: true, theme });
      return;
    }
    throw new Error(`未知 packs 子命令：${sub}`);
  }

  if (cmd === 'custom') {
    if (sub !== 'add') throw new Error(`未知 custom 子命令：${sub}`);
    const theme = createCustomTheme(dataDir, {
      name: flags.name,
      introPath: flags.intro,
      loopPath: flags.loop,
      imagePath: flags.image,
      fadeStartSeconds: flags['fade-start'],
      description: flags.description,
      copyAssets: !flags['no-copy'],
    });
    await callOverlay(dataDir, 'POST', '/reload', {});
    printJson({ ok: true, theme });
    return;
  }

  if (cmd === 'autostart') {
    if (sub === 'on') {
      printJson(setAutostart(true, OVERLAY_EXE, dataDir));
      return;
    }
    if (sub === 'off') {
      printJson(setAutostart(false, OVERLAY_EXE, dataDir));
      return;
    }
    printJson(getAutostartStatus());
    return;
  }

  if (cmd === 'logs') {
    printJson({ logPath: pathsFor(dataDir).logPath, lines: readLogsTail(dataDir, 80) });
    return;
  }

  throw new Error(`未知命令：${cmd}`);
}

main().catch((err) => {
  console.error(err?.message || String(err));
  process.exit(1);
});
