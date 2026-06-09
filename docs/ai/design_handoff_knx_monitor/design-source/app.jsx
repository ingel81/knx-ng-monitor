/* KNX-NG-Monitor — interactive prototype shell */
const { useState: uS, useEffect: uE, useMemo: uM, useRef: uR } = React;

/* ---------------- filtering logic (shared by both pages) ---------------- */
function useFilters() {
  const [q, setQ] = uS('');
  const [time, setTime] = uS('all');
  const [types, setTypes] = uS([]);
  const [topics, setTopics] = uS([]);
  const [visibleCols, setVisibleCols] = uS(ALL_COLUMNS.map(c => c.key));
  const toggleType = k => setTypes(t => t.includes(k) ? t.filter(x => x !== k) : [...t, k]);
  const toggleTopic = k => setTopics(t => t.includes(k) ? t.filter(x => x !== k) : [...t, k]);
  const toggleCol = k => setVisibleCols(c => c.includes(k) ? c.filter(x => x !== k) : ALL_COLUMNS.filter(col => c.includes(col.key) || col.key === k).map(col => col.key));
  const reset = () => { setQ(''); setTime('all'); setTypes([]); setTopics([]); };
  const activeCount = (q ? 1 : 0) + (time !== 'all' ? 1 : 0) + types.length + topics.length;
  return { q, setQ, time, setTime, types, toggleType, topics, toggleTopic, visibleCols, toggleCol, reset, activeCount };
}

function applyFilters(rows, f) {
  const now = Date.now();
  const span = { '1h': 3600e3, '24h': 86400e3, '7d': 7 * 86400e3 }[f.time];
  const topicMatchers = QUICK_TOPICS.filter(t => f.topics.includes(t.key)).map(t => t.match);
  const ql = f.q.trim().toLowerCase();
  return rows.filter(r => {
    if (span && now - r.ts > span) return false;
    if (f.types.length && !f.types.includes(r.type)) return false;
    if (topicMatchers.length && !topicMatchers.some(m => m.test(r.name) || m.test(r.unit))) return false;
    if (ql) {
      const hay = `${r.time} ${r.datetime} ${r.src} ${r.dst} ${r.name} ${r.dpt} ${r.type} ${r.raw} ${r.value} ${r.unit}`.toLowerCase();
      if (!hay.includes(ql)) return false;
    }
    return true;
  });
}

/* ---------------- live stream hook ---------------- */
function useLiveStream(connected, paused) {
  const [rows, setRows] = uS(() => KNXData.buildHistory(60));
  const [newIds, setNewIds] = uS(() => new Set());
  uE(() => {
    if (!connected || paused) return;
    const id = setInterval(() => {
      const batch = Array.from({ length: 1 + Math.floor(Math.random() * 2) }, () => KNXData.makeTelegram());
      setRows(prev => [...batch.reverse(), ...prev].slice(0, 600));
      const ids = new Set(batch.map(b => b.id));
      setNewIds(ids);
      setTimeout(() => setNewIds(new Set()), 550);
    }, 950);
    return () => clearInterval(id);
  }, [connected, paused]);
  const clear = () => setRows([]);
  return { rows, newIds, clear };
}

/* ---------------- effective layout from viewport ---------------- */
function layoutFor(vp) { return vp === 'mobile' ? 'cards' : vp === 'tablet' ? 'reduced' : 'full'; }
function colsFor(vp, visible) {
  if (vp === 'tablet') return visible.filter(k => ['time', 'dst', 'name', 'type', 'val'].includes(k));
  return visible;
}

/* ================================================================ LIVE */
function LiveView({ vp, filters, density, setDensity, detail, setDetail }) {
  const [connected, setConnected] = uS(true);
  const [paused, setPaused] = uS(false);
  const [autoscroll, setAutoscroll] = uS(true);
  const { rows, newIds, clear } = useLiveStream(connected, paused);
  const filtered = uM(() => applyFilters(rows, filters), [rows, filters.q, filters.time, filters.types, filters.topics]);
  const scrollRef = uR(null);
  uE(() => { if (autoscroll && !paused && scrollRef.current) scrollRef.current.scrollTop = 0; }, [filtered.length, autoscroll, paused]);

  return (
    <div className="page">
      <div className="toolbar">
        <div className="tb-left">
          <button className={`knx-btn ${connected ? 'knx-btn--danger' : 'knx-btn--primary'}`} onClick={() => setConnected(c => !c)}>
            <Icon name={connected ? 'disconnect' : 'link'} size={16} /> {connected ? 'Trennen' : 'Verbinden'}
          </button>
          <button className="knx-btn knx-btn--outline" onClick={() => setPaused(p => !p)} disabled={!connected}>
            <Icon name={paused ? 'play' : 'pause'} size={16} /> {paused ? 'Fortsetzen' : 'Pause'}
          </button>
          <label className="knx-switch"><input type="checkbox" checked={autoscroll} onChange={e => setAutoscroll(e.target.checked)} /><span className="track"></span> Auto-scroll</label>
        </div>
        <div className="tb-right">
          <span className="knx-status knx-status--live" style={{ opacity: connected && !paused ? 1 : .4 }}><span className="led"></span> {connected ? (paused ? 'Pausiert' : 'Live') : 'Getrennt'}</span>
          <span className="knx-badge knx-badge--count">{filtered.length.toLocaleString('de-DE')}</span>
          <span className="tb-divider"></span>
          <DensityToggle density={density} setDensity={setDensity} vp={vp} />
          <button className="knx-btn knx-btn--ghost knx-btn--sm" onClick={clear}><Icon name="clear" size={15} /> {vp !== 'mobile' && 'Clear'}</button>
          <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={15} /> {vp !== 'mobile' && 'Export'}</button>
        </div>
      </div>
      <FilterBar {...filters} setQ={filters.setQ} setTime={filters.setTime} toggleType={filters.toggleType} toggleTopic={filters.toggleTopic} toggleCol={filters.toggleCol} onReset={filters.reset} />
      <div className="grid-host" ref={scrollRef}>
        <Grid rows={filtered} layout={layoutFor(vp)} density={density} sort={{ key: 'time', dir: 'desc' }} onSort={() => {}}
          visibleCols={colsFor(vp, filters.visibleCols)} useDateTime={false} onRowClick={setDetail} newIds={newIds} />
      </div>
    </div>
  );
}

/* ================================================================ HISTORY */
function HistoryView({ vp, filters, density, setDensity, detail, setDetail }) {
  const [rows] = uS(() => KNXData.buildHistory(14416 > 800 ? 800 : 800)); // cap render set; show "total" separately
  const TOTAL = 14416;
  const [sort, setSort] = uS({ key: 'time', dir: 'desc' });
  const onSort = key => setSort(s => s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' });
  const filtered = uM(() => {
    let out = applyFilters(rows, filters);
    const dir = sort.dir === 'asc' ? 1 : -1;
    out = [...out].sort((a, b) => {
      let av = a[sort.key], bv = b[sort.key];
      if (sort.key === 'time') { av = a.ts; bv = b.ts; }
      if (sort.key === 'val') { av = parseFloat(String(a.value).replace(/\./g, '').replace(',', '.')) || a.value; bv = parseFloat(String(b.value).replace(/\./g, '').replace(',', '.')) || b.value; }
      return av < bv ? -dir : av > bv ? dir : 0;
    });
    return out;
  }, [rows, filters.q, filters.time, filters.types, filters.topics, sort]);

  return (
    <div className="page">
      <div className="toolbar">
        <div className="tb-left">
          <h2 className="page-title">History</h2>
          <span className="knx-badge knx-badge--count">{TOTAL.toLocaleString('de-DE')}</span>
          {filters.activeCount > 0 && <span className="knx-badge"><Icon name="filter" size={12} /> {filtered.length.toLocaleString('de-DE')} gefiltert</span>}
        </div>
        <div className="tb-right">
          <DensityToggle density={density} setDensity={setDensity} vp={vp} />
          <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={15} /> {vp !== 'mobile' ? 'Export CSV' : ''}</button>
        </div>
      </div>
      <FilterBar {...filters} setQ={filters.setQ} setTime={filters.setTime} toggleType={filters.toggleType} toggleTopic={filters.toggleTopic} toggleCol={filters.toggleCol} onReset={filters.reset} />
      <div className="grid-host">
        <Grid rows={filtered} layout={layoutFor(vp)} density={density} sort={sort} onSort={onSort}
          visibleCols={colsFor(vp, filters.visibleCols)} useDateTime={true} onRowClick={setDetail} newIds={null} />
      </div>
    </div>
  );
}

function DensityToggle({ density, setDensity, vp }) {
  if (vp === 'mobile') return null;
  return (
    <span className="knx-seg">
      <button className={density === 'compact' ? 'is-active' : ''} onClick={() => setDensity('compact')} title="Dicht">Dicht</button>
      <button className={density === 'cozy' ? 'is-active' : ''} onClick={() => setDensity('cozy')} title="Komfortabel">Komfort</button>
    </span>
  );
}

/* ================================================================ PLACEHOLDER PAGES */
function Placeholder({ icon, title, text }) {
  return (
    <div className="placeholder">
      <Icon name={icon} size={40} />
      <h2>{title}</h2>
      <p>{text}</p>
    </div>
  );
}

/* ================================================================ APP SHELL */
const NAV = [
  { key: 'live', label: 'Live View', icon: 'live' },
  { key: 'history', label: 'History', icon: 'history' },
  { key: 'projects', label: 'Projects', icon: 'folder' },
  { key: 'settings', label: 'Settings', icon: 'settings' },
];

function KNXApp({ vp, theme, setTheme }) {
  const [page, setPage] = uS('live');
  const [density, setDensity] = uS('compact');
  const [detail, setDetail] = uS(null);
  const liveFilters = useFilters();
  const histFilters = useFilters();
  uE(() => setDetail(null), [page, vp]);

  return (
    <div className={`knx-app vp-${vp} theme-${theme}`}>
      <header className="app-header on-dark">
        <div className="brand">
          <span className="wm"><span className="k">KNX</span><span className="x">·NG</span></span>
          <span className="brand-sub">Monitor</span>
        </div>
        {vp !== 'mobile' && (
          <nav className="top-nav">
            {NAV.map(n => (
              <button key={n.key} className={`nav-link ${page === n.key ? 'active' : ''}`} onClick={() => setPage(n.key)}>
                <Icon name={n.icon} size={18} /> <span>{n.label}</span>
              </button>
            ))}
          </nav>
        )}
        <div className="user">
          {vp === 'desktop' && <span className="uname">joerg</span>}
          <button className="knx-btn knx-btn--icon knx-btn--ghost"><Icon name="user" size={18} /></button>
          <button className="knx-btn knx-btn--icon knx-btn--ghost"><Icon name="logout" size={18} /></button>
        </div>
      </header>

      <main className="app-main">
        {page === 'live' && <LiveView vp={vp} filters={liveFilters} density={density} setDensity={setDensity} detail={detail} setDetail={setDetail} />}
        {page === 'history' && <HistoryView vp={vp} filters={histFilters} density={density} setDensity={setDensity} detail={detail} setDetail={setDetail} />}
        {page === 'projects' && <ProjectsView vp={vp} />}
        {page === 'settings' && <SettingsView vp={vp} theme={theme} setTheme={setTheme} density={density} setDensity={setDensity} />}
      </main>

      {vp === 'mobile' && (
        <nav className="bottom-nav">
          {NAV.map(n => (
            <button key={n.key} className={page === n.key ? 'active' : ''} onClick={() => setPage(n.key)}>
              <Icon name={n.icon} size={21} /><span>{n.label}</span>
            </button>
          ))}
        </nav>
      )}

      {detail && <DetailSheet row={detail} onClose={() => setDetail(null)} useDateTime={page === 'history'} />}
    </div>
  );
}

/* ================================================================ PREVIEW CHROME */
const VIEWPORTS = [
  { key: 'desktop', label: 'Desktop', icon: 'columns', w: 1280, h: 800 },
  { key: 'tablet', label: 'Tablet', icon: 'grid', w: 834, h: 1000 },
  { key: 'mobile', label: 'Mobil', icon: 'live', w: 390, h: 800 },
];

function Preview() {
  const [vp, setVp] = uS('desktop');
  const [theme, setTheme] = uS('light');
  const [scale, setScale] = uS(1);
  const stageRef = uR(null);
  const cfg = VIEWPORTS.find(v => v.key === vp);
  uE(() => {
    function fit() {
      const el = stageRef.current; if (!el) return;
      const pad = 48;
      const aw = el.clientWidth - pad, ah = el.clientHeight - pad;
      const border = vp === 'mobile' ? 18 : vp === 'tablet' ? 20 : 0;
      const s = Math.min(1, aw / (cfg.w + border), ah / (cfg.h + border));
      setScale(s > 0 ? s : 1);
    }
    fit();
    const ro = new ResizeObserver(fit);
    if (stageRef.current) ro.observe(stageRef.current);
    window.addEventListener('resize', fit);
    return () => { ro.disconnect(); window.removeEventListener('resize', fit); };
  }, [vp]);
  return (
    <div className="preview-root">
      <div className="preview-bar">
        <div className="pv-left">
          <span className="pv-title">KNX-NG-Monitor · Prototyp</span>
          <a className="pv-doclink" href="KNX Design System.html"><Icon name="chevron" size={13} style={{ transform: 'rotate(180deg)' }} /> Designsystem</a>
        </div>
        <span className="knx-seg pv-vp">
          {VIEWPORTS.map(v => (
            <button key={v.key} className={vp === v.key ? 'is-active' : ''} onClick={() => setVp(v.key)}>
              <Icon name={v.icon} size={15} /> {v.label}
            </button>
          ))}
        </span>
        <span className="pv-theme" title="Theme umschalten">
          <button onClick={() => setTheme(t => t === 'light' ? 'dark' : 'light')}>
            <Icon name={theme === 'light' ? 'moon' : 'sun'} size={16} />
          </button>
        </span>
        <span className="pv-dim mono">{cfg.w}×{cfg.h} · {Math.round(scale * 100)}%</span>
      </div>
      <div className="preview-stage knx-scroll" ref={stageRef}>
        <div className="device-scaler" style={{ width: cfg.w * scale, height: cfg.h * scale }}>
          <div className={`device-frame frame-${vp} theme-${theme}`} style={{ width: cfg.w, height: cfg.h, transform: `scale(${scale})`, transformOrigin: 'top left' }}>
            <KNXApp vp={vp} theme={theme} setTheme={setTheme} />
          </div>
        </div>
      </div>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<Preview />);
