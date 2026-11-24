(() => {
  const esc = (s) =>
    String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const api = async (url, method = 'GET', body = null) => {
    const opts = { method };
    if (body !== null && method !== 'GET') opts.body = JSON.stringify(body);
    const res = await window.sidecar.fetch(url, opts);
    const data = await res.json();
    if (!res.ok || data?.ok === false) {
      throw new Error(data?.error || `请求失败 (${res.status})`);
    }
    return data;
  };

  let state = null;
  let activeTab = 'stage';
  let manualTheme = null;
  let sliderTimer = null;
  let pollTimer = null;
  let packPollTimer = null;

  function showToast(msg) {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.classList.remove('hidden');
    clearTimeout(el._t);
    el._t = setTimeout(() => el.classList.add('hidden'), 2600);
  }

  function syncTheme() {
    if (manualTheme) return;
    const probe = document.createElement('span');
    probe.style.cssText = 'position:absolute;visibility:hidden;color:var(--background, #0b1020)';
    document.documentElement.appendChild(probe);
    const rgb = getComputedStyle(probe).color;
    probe.remove();
    const m = rgb.match(/\d+/g);
    if (m && m.length >= 3) {
      const lum = (0.299 * Number(m[0]) + 0.587 * Number(m[1]) + 0.114 * Number(m[2])) / 255;
      document.documentElement.setAttribute('data-theme', lum > 0.5 ? 'light' : 'dark');
    }
  }

  function formatPhase(stage, alive) {
    if (!alive) return ['off', '引擎离线'];
    const p = stage?.phase || 'idle';
    if (stage?.pausedReason === 'battery-saver') return ['warn', '省电暂停'];
    if (stage?.pausedReason === 'window-hidden') return ['warn', '窗口隐藏休眠'];
    if (p === 'intro') return ['live', '播放开屏中'];
    if (p === 'revealed') return ['live', '动态背景中'];
    if (p === 'ending') return ['warn', '淡出中'];
    return ['', '待命'];
  }

  function activeThemeObj() {
    if (!state) return null;
    return state.themes.find((t) => t.id === state.config.activeThemeId) || state.themes[0];
  }

  function renderHeader() {
    if (!state) return;
    const [cls, label] = formatPhase(state.stage, state.engine.alive);
    const pill = document.getElementById('hdr-stage-pill');
    pill.className = `status-pill ${cls}`;
    pill.textContent = label;

    const meta = document.getElementById('hdr-engine-meta');
    if (state.engine.alive) {
      meta.textContent = `引擎 PID ${state.engine.pid} · 已检测 ${state.windows.length} 个 Antigravity 窗口`;
    } else {
      meta.textContent = state.exeBuilt ? '引擎未连接 · 点击播控按钮将自动拉起' : '等待首次编译引擎…';
    }
  }

  function pushConfigPatch(patch, immediate = false) {
    Object.assign(state.config, patch);
    const send = async () => {
      try {
        await api('/api/config', 'POST', { patch });
      } catch (err) {
        showToast(err.message);
      }
    };
    if (immediate) {
      clearTimeout(sliderTimer);
      send();
    } else {
      clearTimeout(sliderTimer);
      sliderTimer = setTimeout(send, 40);
    }
  }

  function renderStage() {
    const el = document.getElementById('pane-stage');
    const th = activeThemeObj();
    const cfg = state.config;
    const st = state.stage || {};
    const isPlaying = st.active && st.phase !== 'done';

    el.innerHTML = `
      <div class="hero">
        <img class="hero-img" src="/api/preview?themeId=${encodeURIComponent(th?.id || 'stellar-aurora')}" alt="${esc(th?.name)}" />
        <div class="hero-overlay">
          <div class="hero-name">${esc(th?.name || '星穹极光')}</div>
          <div class="hero-desc">${esc(th?.description || '')}</div>
        </div>
      </div>

      <div class="transport">
        <button type="button" class="btn btn-primary" id="btn-play-intro">▶ 播放一次开屏</button>
        <button type="button" class="btn" id="btn-play-loop">♾ 开启循环背景</button>
        <button type="button" class="btn" id="btn-skip" ${st.phase === 'intro' ? '' : 'disabled'}>⏭ 跳过开屏</button>
        <button type="button" class="btn btn-danger" id="btn-stop" ${isPlaying ? '' : 'disabled'}>✕ 淡出关闭</button>
      </div>

      <div class="card">
        <div class="card-title">实时调音台</div>
        <div class="slider-row">
          <div class="slider-head">
            <span>循环背景不透明度</span>
            <span class="slider-val" id="val-alpha">${Math.round(cfg.residualAlpha * 100)}%</span>
          </div>
          <input type="range" id="rng-alpha" min="0" max="80" step="1" value="${Math.round(cfg.residualAlpha * 100)}" />
        </div>
        <div class="slider-row">
          <div class="slider-head">
            <span>开屏音效与配音音量</span>
            <span class="slider-val" id="val-vol">${Math.round(cfg.introVolume * 100)}%</span>
          </div>
          <input type="range" id="rng-vol" min="0" max="100" step="1" value="${Math.round(cfg.introVolume * 100)}" />
        </div>
        <div class="switch-row" style="margin-top:8px;">
          <div class="switch-info">
            <div class="switch-label">画面填充方式</div>
            <div class="switch-hint">超宽屏建议尝试完整居中，避免上下裁剪</div>
          </div>
          <div class="seg" id="seg-stretch">
            <button type="button" data-val="UniformToFill" class="${cfg.stretchMode !== 'Uniform' ? 'active' : ''}">铺满裁切</button>
            <button type="button" data-val="Uniform" class="${cfg.stretchMode === 'Uniform' ? 'active' : ''}">完整居中</button>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-title">打开 Antigravity 时</div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">播放开屏动画</div>
            <div class="switch-hint">检测到新窗口启动时播放开屏段落</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-intro" ${cfg.playIntro ? 'checked' : ''} />
        </div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">保留半透明动态背景</div>
            <div class="switch-hint">透出代码界面后继续点击穿透循环播放</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-loop" ${cfg.loopBackground ? 'checked' : ''} />
        </div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">每次启动随机抽签主题</div>
            <div class="switch-hint">在已安装的可播放主题之间自动轮换</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-shuffle" ${cfg.shuffleOnBoot ? 'checked' : ''} />
        </div>
      </div>
    `;

    el.querySelector('#btn-play-intro').onclick = async () => {
      try {
        await api('/api/play', 'POST', { mode: 'intro' });
        showToast('正在窗口上播放开屏动画');
        setTimeout(refreshStatus, 250);
      } catch (e) { showToast(e.message); }
    };
    el.querySelector('#btn-play-loop').onclick = async () => {
      try {
        await api('/api/play', 'POST', { mode: 'loop' });
        showToast('已开启半透明循环背景');
        setTimeout(refreshStatus, 250);
      } catch (e) { showToast(e.message); }
    };
    el.querySelector('#btn-skip').onclick = async () => {
      try { await api('/api/skip', 'POST', {}); setTimeout(refreshStatus, 250); }
      catch (e) { showToast(e.message); }
    };
    el.querySelector('#btn-stop').onclick = async () => {
      try { await api('/api/stop', 'POST', {}); setTimeout(refreshStatus, 350); }
      catch (e) { showToast(e.message); }
    };

    const rngAlpha = el.querySelector('#rng-alpha');
    const valAlpha = el.querySelector('#val-alpha');
    rngAlpha.oninput = () => {
      valAlpha.textContent = `${rngAlpha.value}%`;
      pushConfigPatch({ residualAlpha: Number(rngAlpha.value) / 100 });
    };

    const rngVol = el.querySelector('#rng-vol');
    const valVol = el.querySelector('#val-vol');
    rngVol.oninput = () => {
      valVol.textContent = `${rngVol.value}%`;
      pushConfigPatch({ introVolume: Number(rngVol.value) / 100 });
    };

    el.querySelectorAll('#seg-stretch button').forEach((b) => {
      b.onclick = () => {
        el.querySelectorAll('#seg-stretch button').forEach((x) => x.classList.remove('active'));
        b.classList.add('active');
        pushConfigPatch({ stretchMode: b.dataset.val }, true);
      };
    });

    el.querySelector('#chk-intro').onchange = (e) => pushConfigPatch({ playIntro: e.target.checked }, true);
    el.querySelector('#chk-loop').onchange = (e) => pushConfigPatch({ loopBackground: e.target.checked }, true);
    el.querySelector('#chk-shuffle').onchange = (e) => pushConfigPatch({ shuffleOnBoot: e.target.checked }, true);
  }

  function renderThemes() {
    const el = document.getElementById('pane-themes');
    const activeId = state.config.activeThemeId;

    const myCards = state.themes.map((t) => {
      const isActive = t.id === activeId;
      const kindLabel = t.kind === 'procedural' ? '内置矢量' : (t.kind === 'image' ? '本地壁纸' : '视频动画');
      return `
        <div class="theme-item ${isActive ? 'active' : ''}">
          <img class="theme-thumb" src="/api/preview?themeId=${encodeURIComponent(t.id)}" alt="${esc(t.name)}" />
          <div class="theme-meta">
            <div class="theme-name-row">
              <span class="theme-name">${esc(t.name)}</span>
              <span class="tag">${kindLabel}</span>
              ${isActive ? '<span class="tag good">使用中</span>' : ''}
            </div>
            <div class="theme-desc">${esc(t.description || '')}</div>
            <div class="theme-actions">
              <button type="button" class="btn btn-sm ${isActive ? '' : 'btn-primary'}" data-act="select" data-id="${esc(t.id)}">${isActive ? '已设为当前' : '设为当前'}</button>
              <button type="button" class="btn btn-sm" data-act="play" data-id="${esc(t.id)}">▶ 立即预览</button>
              ${t.builtin ? '' : `<button type="button" class="btn btn-sm btn-danger" data-act="delete" data-id="${esc(t.id)}">删除</button>`}
            </div>
          </div>
        </div>
      `;
    }).join('');

    const packCards = state.officialPacks.map((p) => {
      const pr = p.progress;
      const downloading = pr && pr.status === 'downloading';
      const hasErr = pr && pr.status === 'error';
      return `
        <div class="theme-item">
          <img class="theme-thumb" src="/api/preview?themeId=${encodeURIComponent(p.id)}" alt="${esc(p.name)}" />
          <div class="theme-meta">
            <div class="theme-name-row">
              <span class="theme-name">${esc(p.name)}</span>
              <span class="tag">${esc(p.sizeHint)}</span>
              ${p.installed ? '<span class="tag good">已下载</span>' : ''}
            </div>
            <div class="theme-desc">${esc(p.subtitle)} · ${esc(p.license)}</div>
            ${downloading ? `
              <div class="progress-wrap">
                <div class="progress-bar"><div class="progress-fill" style="width:${pr.percent || 0}%"></div></div>
                <div class="progress-text">正在下载 ${esc(pr.file || '')} (${pr.percent || 0}%)</div>
              </div>
            ` : ''}
            ${hasErr ? `<div class="progress-text" style="color:var(--abs-danger)">下载出错：${esc(pr.error)}</div>` : ''}
            <div class="theme-actions">
              <button type="button" class="btn btn-sm btn-primary" data-pack="${esc(p.id)}" ${downloading ? 'disabled' : ''}>
                ${downloading ? '下载中…' : (p.installed ? '重新下载更新' : '⬇ 一键下载并应用')}
              </button>
            </div>
          </div>
        </div>
      `;
    }).join('');

    el.innerHTML = `
      <div class="card">
        <div class="card-title">
          <span>我的主题 (${state.themes.length})</span>
          <button type="button" class="btn btn-sm btn-primary" id="btn-new-custom">＋ 创建本地主题</button>
        </div>
        <div class="theme-list">${myCards}</div>
      </div>

      <div class="card">
        <div class="card-title">官方同人素材库</div>
        <div class="notice-box">
          素材源自开源项目 <b>wynneceles/ai-boot-animations</b>，按需直连 GitHub / CDN 拉取至本地数据目录（不随插件仓库打包），素材授权为 <b>CC BY-NC-ND 4.0</b>（仅供个人安装使用，不可商用或改后再发布）。
        </div>
        <div class="theme-list">${packCards}</div>
      </div>
    `;

    el.querySelector('#btn-new-custom').onclick = openCustomModal;

    el.querySelectorAll('[data-act="select"]').forEach((btn) => {
      btn.onclick = async () => {
        try {
          const r = await api('/api/themes/select', 'POST', { themeId: btn.dataset.id });
          state = r.bootstrap;
          renderAll();
          showToast('已切换当前开机主题');
        } catch (e) { showToast(e.message); }
      };
    });

    el.querySelectorAll('[data-act="play"]').forEach((btn) => {
      btn.onclick = async () => {
        try {
          const r = await api('/api/themes/select', 'POST', { themeId: btn.dataset.id, playMode: 'intro' });
          state = r.bootstrap;
          renderAll();
          showToast('已开始预览主题开屏');
        } catch (e) { showToast(e.message); }
      };
    });

    el.querySelectorAll('[data-act="delete"]').forEach((btn) => {
      btn.onclick = async () => {
        try {
          const r = await api(`/api/themes?themeId=${encodeURIComponent(btn.dataset.id)}`, 'DELETE', { themeId: btn.dataset.id });
          state = r.bootstrap;
          renderAll();
          showToast('主题已删除');
        } catch (e) { showToast(e.message); }
      };
    });

    el.querySelectorAll('[data-pack]').forEach((btn) => {
      btn.onclick = async () => {
        try {
          await api('/api/packs/download', 'POST', { packId: btn.dataset.pack, autoActivate: true });
          showToast('开始下载素材包…');
          startPackProgressPoll();
        } catch (e) { showToast(e.message); }
      };
    });
  }

  function startPackProgressPoll() {
    clearInterval(packPollTimer);
    packPollTimer = setInterval(async () => {
      try {
        const res = await api('/api/packs/progress', 'GET');
        state.officialPacks = res.officialPacks;
        const stillBusy = res.officialPacks.some((p) => p.progress && p.progress.status === 'downloading');
        if (activeTab === 'themes') renderThemes();
        if (!stillBusy) {
          clearInterval(packPollTimer);
          await refreshBootstrap();
          showToast('素材包准备就绪');
        }
      } catch {
        clearInterval(packPollTimer);
      }
    }, 450);
  }

  function renderSmart() {
    const el = document.getElementById('pane-smart');
    const cfg = state.config;
    const auto = state.autostart || { enabled: false };
    const winOpts = [
      `<option value="0" ${Number(cfg.lockedWindowHwnd || 0) === 0 ? 'selected' : ''}>自动选择最大的 Antigravity 主窗口</option>`,
      ...state.windows.map((w) => `<option value="${w.hwnd}" ${Number(cfg.lockedWindowHwnd) === Number(w.hwnd) ? 'selected' : ''}>${esc(w.title || w.processName)} (${w.width}x${w.height})</option>`),
    ].join('');

    el.innerHTML = `
      <div class="card">
        <div class="card-title">编码专注自动变淡</div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">专心打字/操作时自动调淡背景</div>
            <div class="switch-hint">防止循环画面干扰阅读代码；停下几秒后优雅回亮</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-focus" ${cfg.focusDimEnabled ? 'checked' : ''} />
        </div>
        <div class="slider-row" style="margin-top:10px;">
          <div class="slider-head">
            <span>专注时保留浓度比例</span>
            <span class="slider-val" id="val-dim">${Math.round(cfg.focusDimFactor * 100)}%</span>
          </div>
          <input type="range" id="rng-dim" min="10" max="100" step="5" value="${Math.round(cfg.focusDimFactor * 100)}" />
        </div>
        <div class="slider-row">
          <div class="slider-head">
            <span>静置回亮等待时间</span>
            <span class="slider-val" id="val-idle">${Number(cfg.focusIdleSeconds).toFixed(1)} 秒</span>
          </div>
          <input type="range" id="rng-idle" min="1" max="20" step="0.5" value="${Number(cfg.focusIdleSeconds)}" />
        </div>
      </div>

      <div class="card">
        <div class="card-title">节能与启动模式</div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">Windows 省电模式自动暂停解码</div>
            <div class="switch-hint">窗口最小化或被遮挡时同样自动零占用休眠</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-battery" ${cfg.pauseOnBatterySaver ? 'checked' : ''} />
        </div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">极速抢跑开机自启 (Windows Startup)</div>
            <div class="switch-hint">关闭 = 纯插件托管（随 Antigravity 启停，零后台残留）；开启 = 登录即驻留，打开 IDE 0 秒开屏</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-autostart" ${auto.enabled ? 'checked' : ''} />
        </div>
        <div class="switch-row">
          <div class="switch-info">
            <div class="switch-label">在系统托盘显示 ✦ 快捷图标</div>
            <div class="switch-hint">通常仅在开启独立自启时需要，插件侧边栏始终可控</div>
          </div>
          <input type="checkbox" class="toggle" id="chk-tray" ${cfg.showTrayIcon ? 'checked' : ''} />
        </div>
      </div>

      <div class="card">
        <div class="card-title">
          <span>目标窗口锁定</span>
          <button type="button" class="btn btn-sm" id="btn-refresh-win">刷新窗口</button>
        </div>
        <div class="field" style="margin-bottom:0;">
          <select id="sel-hwnd">${winOpts}</select>
        </div>
      </div>
    `;

    el.querySelector('#chk-focus').onchange = (e) => pushConfigPatch({ focusDimEnabled: e.target.checked }, true);
    const rngDim = el.querySelector('#rng-dim');
    const valDim = el.querySelector('#val-dim');
    rngDim.oninput = () => {
      valDim.textContent = `${rngDim.value}%`;
      pushConfigPatch({ focusDimFactor: Number(rngDim.value) / 100 });
    };
    const rngIdle = el.querySelector('#rng-idle');
    const valIdle = el.querySelector('#val-idle');
    rngIdle.oninput = () => {
      valIdle.textContent = `${Number(rngIdle.value).toFixed(1)} 秒`;
      pushConfigPatch({ focusIdleSeconds: Number(rngIdle.value) });
    };
    el.querySelector('#chk-battery').onchange = (e) => pushConfigPatch({ pauseOnBatterySaver: e.target.checked }, true);
    el.querySelector('#chk-tray').onchange = (e) => pushConfigPatch({ showTrayIcon: e.target.checked }, true);
    el.querySelector('#chk-autostart').onchange = async (e) => {
      try {
        const res = await api('/api/autostart', 'POST', { enabled: e.target.checked });
        state.autostart = res.autostart;
        showToast(e.target.checked ? '已启用极速开机自启' : '已切回纯插件托管模式');
      } catch (err) {
        e.target.checked = !e.target.checked;
        showToast(err.message);
      }
    };
    el.querySelector('#sel-hwnd').onchange = (e) => pushConfigPatch({ lockedWindowHwnd: Number(e.target.value) }, true);
    el.querySelector('#btn-refresh-win').onclick = refreshBootstrap;
  }

  function renderDiag() {
    const el = document.getElementById('pane-diag');
    const prevBox = document.getElementById('log-box');
    const prevTop = prevBox ? prevBox.scrollTop : null;
    const atBottom = prevBox ? (prevBox.scrollHeight - prevBox.scrollTop - prevBox.clientHeight < 20) : true;
    const st = state.stage || {};
    const eng = state.engine || {};
    const logs = (state.logs || []).join('\n') || '暂无日志记录';

    el.innerHTML = `
      <div class="metrics-grid">
        <div class="metric-box">
          <div class="metric-k">引擎状态</div>
          <div class="metric-v">${eng.alive ? `在线 (PID ${eng.pid})` : '离线'}</div>
        </div>
        <div class="metric-box">
          <div class="metric-k">环回控制端口</div>
          <div class="metric-v">${eng.port ? `127.0.0.1:${eng.port}` : '—'}</div>
        </div>
        <div class="metric-box">
          <div class="metric-k">循环圈数 / 自愈</div>
          <div class="metric-v">${st.laps ?? 0} 圈 / ${st.recoveries ?? 0} 次</div>
        </div>
        <div class="metric-box">
          <div class="metric-k">内存 / 当前不透明度</div>
          <div class="metric-v">${st.memoryMb ?? 0} MB / ${Math.round((st.effectiveAlpha || 0) * 100)}%</div>
        </div>
      </div>

      <div class="card">
        <div class="card-title">维护操作</div>
        <div class="theme-actions">
          <button type="button" class="btn btn-sm btn-primary" id="btn-restart-engine">🔄 重启浮层引擎</button>
          <button type="button" class="btn btn-sm" id="btn-open-dir">📂 打开数据目录</button>
          <button type="button" class="btn btn-sm" id="btn-refresh-logs">刷新状态与日志</button>
        </div>
        <div class="switch-hint" style="margin-top:8px;">数据目录：${esc(state.dataDir)}</div>
      </div>

      <div class="card">
        <div class="card-title">最近引擎日志</div>
        <div class="log-box" id="log-box">${esc(logs)}</div>
      </div>
    `;

    const logBox = document.getElementById('log-box');
    if (logBox) {
      if (atBottom) logBox.scrollTop = logBox.scrollHeight;
      else if (prevTop !== null) logBox.scrollTop = prevTop;
    }

    el.querySelector('#btn-restart-engine').onclick = async () => {
      try {
        showToast('正在重启浮层引擎…');
        const res = await api('/api/engine/restart', 'POST', {});
        state = res.bootstrap;
        renderAll();
        showToast(res.ok ? '浮层引擎已重启就绪' : (res.error || '重启失败'));
      } catch (e) { showToast(e.message); }
    };
    el.querySelector('#btn-open-dir').onclick = async () => {
      try { await api('/api/open-data-dir', 'POST', {}); }
      catch (e) { showToast(e.message); }
    };
    el.querySelector('#btn-refresh-logs').onclick = refreshBootstrap;
  }

  function openCustomModal() {
    const bd = document.getElementById('modal-backdrop');
    const body = document.getElementById('modal-body');
    const foot = document.getElementById('modal-foot');

    body.innerHTML = `
      <div class="field">
        <label>主题名称</label>
        <input class="input" id="cm-name" placeholder="例如：赛博霓虹 / 我的专属开屏" />
      </div>
      <div class="field">
        <label>媒体类型</label>
        <div class="seg" id="cm-kind">
          <button type="button" data-kind="video" class="active">视频主题 (MP4/WMV)</button>
          <button type="button" data-kind="image">图片壁纸 (PNG/JPG)</button>
        </div>
      </div>
      <div id="cm-video-fields">
        <div class="field">
          <label>开屏视频路径（可选，支持音轨）</label>
          <div class="input-row">
            <input class="input" id="cm-intro" placeholder="选择本地 MP4 / WMV..." />
            <button type="button" class="btn btn-sm" id="pick-intro">浏览</button>
          </div>
        </div>
        <div class="field">
          <label>循环背景视频路径（可选，建议静音且首尾衔接）</label>
          <div class="input-row">
            <input class="input" id="cm-loop" placeholder="选择本地 MP4 / WMV..." />
            <button type="button" class="btn btn-sm" id="pick-loop">浏览</button>
          </div>
        </div>
      </div>
      <div id="cm-image-fields" class="hidden">
        <div class="field">
          <label>本地壁纸路径（自动叠加微呼吸动态缩放）</label>
          <div class="input-row">
            <input class="input" id="cm-image" placeholder="选择本地 PNG / JPG / WEBP..." />
            <button type="button" class="btn btn-sm" id="pick-image">浏览</button>
          </div>
        </div>
      </div>
      <div class="field">
        <label>淡出透出界面时间点（秒）</label>
        <input class="input" id="cm-fade" type="number" step="0.1" value="8.0" />
      </div>
      <div class="switch-row" style="border-bottom:none;">
        <div class="switch-info">
          <div class="switch-label">复制文件到工坊数据目录</div>
          <div class="switch-hint">防止以后移动或删除原文件导致主题失效</div>
        </div>
        <input type="checkbox" class="toggle" id="cm-copy" checked />
      </div>
    `;

    foot.innerHTML = `
      <button type="button" class="btn" id="cm-cancel">取消</button>
      <button type="button" class="btn btn-primary" id="cm-save">保存并应用</button>
    `;

    let mode = 'video';
    body.querySelectorAll('#cm-kind button').forEach((b) => {
      b.onclick = () => {
        mode = b.dataset.kind;
        body.querySelectorAll('#cm-kind button').forEach((x) => x.classList.remove('active'));
        b.classList.add('active');
        body.querySelector('#cm-video-fields').classList.toggle('hidden', mode !== 'video');
        body.querySelector('#cm-image-fields').classList.toggle('hidden', mode !== 'image');
        body.querySelector('#cm-fade').value = mode === 'image' ? '3.0' : '8.0';
      };
    });

    const pickInto = async (inputId, kind) => {
      try {
        const res = await api('/api/pick-file', 'POST', { kind });
        if (!res.canceled && res.path) body.querySelector(inputId).value = res.path;
      } catch (e) { showToast(e.message); }
    };
    body.querySelector('#pick-intro').onclick = () => pickInto('#cm-intro', 'video');
    body.querySelector('#pick-loop').onclick = () => pickInto('#cm-loop', 'video');
    body.querySelector('#pick-image').onclick = () => pickInto('#cm-image', 'image');

    foot.querySelector('#cm-cancel').onclick = () => bd.classList.add('hidden');
    foot.querySelector('#cm-save').onclick = async () => {
      try {
        const payload = {
          name: body.querySelector('#cm-name').value.trim(),
          introPath: mode === 'video' ? body.querySelector('#cm-intro').value.trim() : '',
          loopPath: mode === 'video' ? body.querySelector('#cm-loop').value.trim() : '',
          imagePath: mode === 'image' ? body.querySelector('#cm-image').value.trim() : '',
          fadeStartSeconds: Number(body.querySelector('#cm-fade').value || 8.0),
          copyAssets: body.querySelector('#cm-copy').checked,
          activate: true,
        };
        const res = await api('/api/themes/custom', 'POST', payload);
        state = res.bootstrap;
        bd.classList.add('hidden');
        renderAll();
        showToast('自定义主题已保存并启用');
      } catch (e) {
        showToast(e.message);
      }
    };

    bd.classList.remove('hidden');
  }

  function renderAll() {
    if (!state) return;
    renderHeader();
    if (activeTab === 'stage') renderStage();
    else if (activeTab === 'themes') renderThemes();
    else if (activeTab === 'smart') renderSmart();
    else if (activeTab === 'diag') renderDiag();
  }

  async function refreshBootstrap() {
    try {
      state = await api('/api/bootstrap', 'GET');
      renderAll();
    } catch (err) {
      showToast(err.message);
    }
  }

  async function refreshStatus() {
    try {
      const next = await api('/api/status', 'GET');
      state = next;
      renderHeader();
      if (activeTab === 'diag') renderDiag();
      if (activeTab === 'stage') {
        const skipBtn = document.getElementById('btn-skip');
        const stopBtn = document.getElementById('btn-stop');
        if (skipBtn) skipBtn.disabled = state.stage?.phase !== 'intro';
        if (stopBtn) stopBtn.disabled = !(state.stage?.active && state.stage?.phase !== 'done');
      }
    } catch { }
  }

  document.querySelectorAll('.tab-btn').forEach((btn) => {
    btn.onclick = () => {
      activeTab = btn.dataset.tab;
      document.querySelectorAll('.tab-btn').forEach((b) => b.classList.toggle('active', b === btn));
      document.querySelectorAll('.pane').forEach((p) => p.classList.toggle('hidden', p.id !== `pane-${activeTab}`));
      renderAll();
    };
  });

  document.getElementById('btn-theme-toggle').onclick = () => {
    const cur = document.documentElement.getAttribute('data-theme') || 'dark';
    manualTheme = cur === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', manualTheme);
  };

  document.getElementById('modal-close').onclick = () => document.getElementById('modal-backdrop').classList.add('hidden');
  document.getElementById('modal-backdrop').onclick = (e) => {
    if (e.target.id === 'modal-backdrop') e.target.classList.add('hidden');
  };

  window.addEventListener('message', (ev) => {
    if (ev.data?.type === 'theme-change') syncTheme();
  });

  syncTheme();
  refreshBootstrap();
  pollTimer = setInterval(refreshStatus, 2500);
})();
