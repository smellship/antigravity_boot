import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  OFFICIAL_PACKS,
  callOverlay,
  createCustomTheme,
  deleteTheme,
  downloadOfficialPack,
  getAutostartStatus,
  isOverlayAlive,
  isPackInstalled,
  loadConfig,
  loadThemes,
  patchConfig,
  pathsFor,
  readLogsTail,
  readRuntime,
  resolveDataDir,
  setAutostart,
} from '../../scripts/studio_core.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = join(__dirname, '..', '..');
const BIN_EXE = join(PLUGIN_ROOT, 'bin', 'AntigravityBootOverlay.exe');
const BUILD_SCRIPT = join(PLUGIN_ROOT, 'scripts', 'build_engine.ps1');
const LOGO_PATH = join(PLUGIN_ROOT, 'assets', 'logo.svg');

function parseCliFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const k = a.slice(2);
      const next = argv[i + 1];
      if (next !== undefined && !next.startsWith('--')) {
        flags[k] = next;
        i++;
      } else {
        flags[k] = true;
      }
    }
  }
  return flags;
}

const CLI_FLAGS = parseCliFlags(process.argv.slice(2));
const IS_STANDALONE = Boolean(CLI_FLAGS.standalone) || !process.env.ANTIGRAVITY_SIDECAR_WEB_PORT;
const DATA_DIR = resolveDataDir(typeof CLI_FLAGS['data-dir'] === 'string' ? CLI_FLAGS['data-dir'] : '');
const NO_CACHE = { 'Cache-Control': 'no-store' };

class LocalResponse {
  constructor(body, { contentType = 'text/plain', status = 200, headers } = {}) {
    this.body = body;
    this.contentType = contentType;
    this.status = status;
    this.headers = headers;
  }
}

class StandaloneApp {
  #pages = new Map();
  #apis = new Map();
  #port;

  constructor(port) {
    this.#port = Number(port || 47919);
  }

  get config() {
    return { projectId: '', conversationId: '' };
  }

  page(path, handler) {
    this.#pages.set(path, handler);
  }

  api(path, handler, method = 'POST') {
    if (!this.#apis.has(path)) this.#apis.set(path, new Map());
    this.#apis.get(path).set(method.toUpperCase(), handler);
  }

  run() {
    this.#pages.set('/preload.js', () => new LocalResponse(
      `window.sidecar = { fetch: (u, o = {}) => fetch(u, { ...o, headers: { 'Content-Type': 'application/json', ...(o.headers || {}) } }), getWorkspaceUris: async () => [] };`,
      { contentType: 'application/javascript; charset=utf-8', headers: NO_CACHE }
    ));

    const server = createServer(async (req, res) => {
      const url = new URL(req.url, `http://127.0.0.1:${this.#port}`);
      if (req.method === 'GET' && this.#pages.has(url.pathname)) {
        const out = await this.#pages.get(url.pathname)();
        if (out instanceof LocalResponse || (out && typeof out === 'object' && out.contentType && out.body !== undefined)) {
          res.writeHead(out.status || 200, { 'Content-Type': out.contentType, ...(out.headers || {}) });
          res.end(out.body);
          return;
        }
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(out);
        return;
      }

      if (this.#apis.has(url.pathname)) {
        const mMap = this.#apis.get(url.pathname);
        if (mMap.has(req.method)) {
          let body = '';
          req.on('data', (c) => { body += c; });
          req.on('end', async () => {
            try {
              const data = {};
              for (const [k, v] of url.searchParams.entries()) data[k] = v;
              if (body) Object.assign(data, JSON.parse(body));
              const out = await mMap.get(req.method)(data);
              if (out && typeof out === 'object' && out.contentType && out.body !== undefined) {
                res.writeHead(out.status || 200, { 'Content-Type': out.contentType, ...(out.headers || {}) });
                res.end(typeof out.body === 'string' ? out.body : Buffer.from(out.body));
                return;
              }
              res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
              res.end(JSON.stringify(out));
            } catch (err) {
              res.writeHead(500, { 'Content-Type': 'application/json; charset=utf-8' });
              res.end(JSON.stringify({ ok: false, error: err.message }));
            }
          });
          return;
        }
      }

      res.writeHead(404);
      res.end('Not found');
    });

    return new Promise((resolveReady) => {
      server.listen(this.#port, '127.0.0.1', () => {
        console.log(`[boot-studio] standalone listening on http://127.0.0.1:${server.address().port}`);
        resolveReady(server);
      });
    });
  }
}

async function createAppHost() {
  if (!IS_STANDALONE) {
    const sdk = await import('sidecar_sdk');
    return { app: new sdk.SidecarApp(), ResponseClass: sdk.Response };
  }
  return { app: new StandaloneApp(CLI_FLAGS.port || 47919), ResponseClass: LocalResponse };
}

// ---------------------------------------------------------------------------
// Engine Supervisor & Download Progress State
// ---------------------------------------------------------------------------
const packProgress = new Map();
let engineSpawnPromise = null;

function ensureEngineBuilt() {
  if (existsSync(BIN_EXE)) return true;
  if (!existsSync(BUILD_SCRIPT)) return false;
  try {
    console.log('[boot-studio] compiling AntigravityBootOverlay.exe ...');
    execFileSync(
      'powershell.exe',
      ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', BUILD_SCRIPT, '-OutDir', join(PLUGIN_ROOT, 'bin')],
      { encoding: 'utf-8', timeout: 60000 }
    );
    return existsSync(BIN_EXE);
  } catch (err) {
    console.error('[boot-studio] build failed:', err.message);
    return false;
  }
}

async function ensureOverlayRunning({ autoPlayOnStart = false, forceRestart = false } = {}) {
  if (engineSpawnPromise) return engineSpawnPromise;
  engineSpawnPromise = (async () => {
    try {
      if (forceRestart && (await isOverlayAlive(DATA_DIR))) {
        await callOverlay(DATA_DIR, 'POST', '/shutdown', {});
        await new Promise((r) => setTimeout(r, 600));
      }
      if (await isOverlayAlive(DATA_DIR)) {
        return { ok: true, alreadyRunning: true };
      }
      if (!ensureEngineBuilt()) {
        return { ok: false, error: '未找到或无法编译 AntigravityBootOverlay.exe' };
      }

      mkdirSync(DATA_DIR, { recursive: true });
      const args = ['--managed', '--parent-pid', String(process.pid), '--data-dir', DATA_DIR];
      if (autoPlayOnStart) args.push('--auto-play-on-start');

      const child = spawn(BIN_EXE, args, {
        detached: true,
        stdio: 'ignore',
        windowsHide: true,
      });
      child.unref();

      for (let i = 0; i < 15; i++) {
        await new Promise((r) => setTimeout(r, 200));
        if (await isOverlayAlive(DATA_DIR)) return { ok: true, pid: child.pid };
      }
      return { ok: false, error: '引擎已拉起但等待控制端口就绪超时' };
    } finally {
      engineSpawnPromise = null;
    }
  })();
  return engineSpawnPromise;
}

function buildOfficialPacksView() {
  return OFFICIAL_PACKS.map((p) => ({
    id: p.id,
    name: p.name,
    subtitle: p.subtitle,
    description: p.description,
    author: p.author,
    license: p.license,
    repoUrl: p.repoUrl,
    sizeHint: p.sizeHint,
    accentColor: p.accentColor,
    installed: isPackInstalled(DATA_DIR, p.id),
    progress: packProgress.get(p.id) || null,
  }));
}

async function buildBootstrapPayload() {
  const cfg = loadConfig(DATA_DIR);
  const themes = loadThemes(DATA_DIR);
  const alive = await isOverlayAlive(DATA_DIR);
  const live = alive ? await callOverlay(DATA_DIR, 'GET', '/status') : null;
  const rt = readRuntime(DATA_DIR);

  return {
    ok: true,
    dataDir: DATA_DIR,
    exeBuilt: existsSync(BIN_EXE),
    standalone: IS_STANDALONE,
    engine: {
      alive,
      pid: live?.pid || rt?.pid || null,
      port: rt?.port || null,
      managed: rt?.managed ?? true,
    },
    stage: live?.stage || {
      active: false,
      phase: alive ? 'idle' : 'offline',
      themeId: cfg.activeThemeId,
      laps: 0,
      recoveries: 0,
      effectiveAlpha: 0,
      pausedReason: '',
      memoryMb: 0,
    },
    windows: live?.windows || [],
    config: cfg,
    themes,
    officialPacks: buildOfficialPacksView(),
    autostart: getAutostartStatus(),
    logs: live?.logTail || readLogsTail(DATA_DIR, 40),
  };
}

function svgCardForTheme(title, subtitle, accent = '#60A5FA') {
  const esc = (s) => String(s || '').replace(/[<>&"]/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' }[c]));
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 360" width="640" height="360">
    <defs>
      <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stop-color="#070B19"/>
        <stop offset="55%" stop-color="#172047"/>
        <stop offset="100%" stop-color="#0B1120"/>
      </linearGradient>
      <radialGradient id="halo" cx="50%" cy="46%" r="48%">
        <stop offset="0%" stop-color="${esc(accent)}" stop-opacity="0.45"/>
        <stop offset="100%" stop-color="${esc(accent)}" stop-opacity="0"/>
      </radialGradient>
    </defs>
    <rect width="640" height="360" fill="url(#g)"/>
    <circle cx="320" cy="166" r="170" fill="url(#halo)"/>
    <ellipse cx="320" cy="166" rx="150" ry="52" transform="rotate(-22 320 166)" fill="none" stroke="${esc(accent)}" stroke-opacity="0.45" stroke-width="2" stroke-dasharray="10 7"/>
    <path d="M180 260 L460 72" stroke="#7DD3FC" stroke-width="3" stroke-linecap="round" opacity="0.75"/>
    <path d="M180 72 L460 260" stroke="#C084FC" stroke-width="3" stroke-linecap="round" opacity="0.7"/>
    <path d="M320 96 C326 144 342 160 390 166 C342 172 326 188 320 236 C314 188 298 172 250 166 C298 160 314 144 320 96 Z" fill="#FFFFFF" opacity="0.95"/>
    <text x="320" y="294" text-anchor="middle" fill="#F8FAFC" font-size="20" font-family="Segoe UI, Microsoft YaHei UI, sans-serif" font-weight="600">${esc(title)}</text>
    <text x="320" y="322" text-anchor="middle" fill="#94A3B8" font-size="13" font-family="Segoe UI, Microsoft YaHei UI, sans-serif">${esc(subtitle)}</text>
  </svg>`;
}

const { app, ResponseClass } = await createAppHost();

app.page('/', () => new ResponseClass(readFileSync(join(__dirname, 'index.html'), 'utf-8'), {
  contentType: 'text/html; charset=utf-8',
  headers: NO_CACHE,
}));
app.api('/styles.css', () => new ResponseClass(readFileSync(join(__dirname, 'styles.css'), 'utf-8'), {
  contentType: 'text/css; charset=utf-8',
  headers: NO_CACHE,
}), 'GET');
app.api('/app.js', () => new ResponseClass(readFileSync(join(__dirname, 'app.js'), 'utf-8'), {
  contentType: 'application/javascript; charset=utf-8',
  headers: NO_CACHE,
}), 'GET');
app.api('/logo.svg', () => new ResponseClass(readFileSync(LOGO_PATH, 'utf-8'), {
  contentType: 'image/svg+xml; charset=utf-8',
  headers: NO_CACHE,
}), 'GET');

app.api('/api/preview', (data) => {
  const id = String(data.themeId || 'stellar-aurora');
  const themes = loadThemes(DATA_DIR);
  const th = themes.find((x) => x.id === id);
  const candidate = th?.previewPath || th?.imagePath || '';
  if (candidate && existsSync(candidate)) {
    const ext = extname(candidate).toLowerCase();
    const mime = ext === '.png' ? 'image/png' : (ext === '.webp' ? 'image/webp' : 'image/jpeg');
    return new ResponseClass(readFileSync(candidate), {
      contentType: mime,
      headers: { 'Cache-Control': 'public, max-age=3600' },
    });
  }
  const pack = OFFICIAL_PACKS.find((p) => p.id === id);
  const svg = svgCardForTheme(th?.name || pack?.name || '✦ 星穹极光', th?.description || pack?.subtitle || 'Antigravity Boot Studio', th?.accentColor || pack?.accentColor || '#60A5FA');
  return new ResponseClass(svg, { contentType: 'image/svg+xml; charset=utf-8', headers: NO_CACHE });
}, 'GET');

app.api('/api/bootstrap', async () => buildBootstrapPayload(), 'GET');
app.api('/api/status', async () => buildBootstrapPayload(), 'GET');
app.api('/api/logs', () => ({ ok: true, lines: readLogsTail(DATA_DIR, 100), logPath: pathsFor(DATA_DIR).logPath }), 'GET');

app.api('/api/play', async (data) => {
  if (!(await isOverlayAlive(DATA_DIR))) {
    const boot = await ensureOverlayRunning({ autoPlayOnStart: false });
    if (!boot.ok) return boot;
  }
  const res = await callOverlay(DATA_DIR, 'POST', '/play', {
    mode: data.mode === 'loop' ? 'loop' : 'intro',
    themeId: data.themeId || null,
    hwnd: Number(data.hwnd || 0),
  });
  return res || { ok: false, error: '与浮层引擎通信失败' };
});

app.api('/api/skip', async () => {
  const res = await callOverlay(DATA_DIR, 'POST', '/skip', {});
  return res || { ok: false, error: '浮层引擎未运行' };
});

app.api('/api/stop', async () => {
  const res = await callOverlay(DATA_DIR, 'POST', '/stop', { fast: true });
  return res || { ok: false, error: '浮层引擎未运行' };
});

app.api('/api/engine/restart', async () => {
  const boot = await ensureOverlayRunning({ autoPlayOnStart: false, forceRestart: true });
  return { ...boot, bootstrap: await buildBootstrapPayload() };
});

app.api('/api/config', async (data) => {
  const patch = data.patch && typeof data.patch === 'object' ? data.patch : data;
  const updated = patchConfig(DATA_DIR, patch);
  const live = await callOverlay(DATA_DIR, 'POST', '/apply-live', patch);
  return { ok: true, config: updated, appliedLive: Boolean(live), liveStatus: live || null };
});

app.api('/api/themes/select', async (data) => {
  const id = String(data.themeId || '');
  if (!id) throw new Error('缺少 themeId');
  const updated = patchConfig(DATA_DIR, { activeThemeId: id });
  await callOverlay(DATA_DIR, 'POST', '/apply-live', { activeThemeId: id });
  if (data.playMode) {
    if (!(await isOverlayAlive(DATA_DIR))) {
      await ensureOverlayRunning({ autoPlayOnStart: false });
    }
    await callOverlay(DATA_DIR, 'POST', '/play', { mode: data.playMode, themeId: id });
  }
  return { ok: true, config: updated, bootstrap: await buildBootstrapPayload() };
});

app.api('/api/themes/custom', async (data) => {
  const theme = createCustomTheme(DATA_DIR, data);
  if (data.activate) {
    patchConfig(DATA_DIR, { activeThemeId: theme.id });
    await callOverlay(DATA_DIR, 'POST', '/apply-live', { activeThemeId: theme.id });
  } else {
    await callOverlay(DATA_DIR, 'POST', '/reload', {});
  }
  return { ok: true, theme, bootstrap: await buildBootstrapPayload() };
});

app.api('/api/themes', async (data) => {
  const id = String(data.themeId || '');
  deleteTheme(DATA_DIR, id);
  await callOverlay(DATA_DIR, 'POST', '/reload', {});
  return { ok: true, bootstrap: await buildBootstrapPayload() };
}, 'DELETE');

app.api('/api/packs/download', async (data) => {
  const packId = String(data.packId || '');
  if (!OFFICIAL_PACKS.some((p) => p.id === packId)) throw new Error(`未知素材包：${packId}`);
  const cur = packProgress.get(packId);
  if (cur && cur.status === 'downloading') {
    return { ok: true, started: true, alreadyDownloading: true };
  }

  packProgress.set(packId, { packId, status: 'downloading', file: '连接仓库...', percent: 1 });
  (async () => {
    try {
      const theme = await downloadOfficialPack(DATA_DIR, packId, (ev) => {
        packProgress.set(packId, ev);
      });
      if (data.autoActivate !== false) {
        patchConfig(DATA_DIR, { activeThemeId: theme.id });
        await callOverlay(DATA_DIR, 'POST', '/apply-live', { activeThemeId: theme.id });
      } else {
        await callOverlay(DATA_DIR, 'POST', '/reload', {});
      }
    } catch (err) {
      packProgress.set(packId, {
        packId,
        status: 'error',
        error: err.message,
        percent: 0,
      });
    }
  })();

  return { ok: true, started: true, packId };
});

app.api('/api/packs/progress', () => ({
  ok: true,
  progress: Object.fromEntries(packProgress.entries()),
  officialPacks: buildOfficialPacksView(),
}), 'GET');

app.api('/api/pick-file', (data) => {
  const kind = data.kind === 'image' ? 'image' : 'video';
  const filter = kind === 'image'
    ? '图片文件 (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件 (*.*)|*.*'
    : '视频文件 (*.mp4;*.wmv;*.mov;*.avi;*.mkv)|*.mp4;*.wmv;*.mov;*.avi;*.mkv|所有文件 (*.*)|*.*';
  const psScript = [
    'Add-Type -AssemblyName System.Windows.Forms',
    '$dlg = New-Object System.Windows.Forms.OpenFileDialog',
    `$dlg.Filter = '${filter}'`,
    '$dlg.RestoreDirectory = $true',
    'if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $dlg.FileName }',
  ].join('; ');
  const chosen = execFileSync('powershell.exe', ['-NoProfile', '-STA', '-Command', psScript], { encoding: 'utf-8' }).trim();
  return { ok: true, canceled: !chosen, path: chosen };
});

app.api('/api/autostart', (data) => {
  if (!existsSync(BIN_EXE)) ensureEngineBuilt();
  const status = setAutostart(Boolean(data.enabled), BIN_EXE, DATA_DIR);
  return { ok: true, autostart: status };
});

app.api('/api/open-data-dir', () => {
  mkdirSync(DATA_DIR, { recursive: true });
  spawn('explorer.exe', [DATA_DIR], { detached: true, stdio: 'ignore' }).unref();
  return { ok: true, dataDir: DATA_DIR };
});

await app.run();

if (!IS_STANDALONE) {
  ensureOverlayRunning({ autoPlayOnStart: true }).catch((err) => {
    console.error('[boot-studio] supervisor error:', err);
  });
}
