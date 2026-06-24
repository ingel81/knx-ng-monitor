/* KNX-NG-Monitor — app shell, login, routing, responsive preview. */
const { useState: uS, useEffect: uE, useMemo: uM, useRef: uR } = React;

const NAV = [
  { key: 'monitor',   icon: 'monitor',  en: 'Monitor',        de: 'Monitor' },
  { key: 'charts',    icon: 'chart',    en: 'Charts',         de: 'Diagramme' },
  { key: 'statistics',icon: 'barchart', en: 'Statistics',     de: 'Statistik' },
  { key: 'projects',  icon: 'folder',   en: 'Projects',       de: 'Projekte' },
  { key: 'topology',  icon: 'share',    en: 'Topology',       de: 'Topologie' },
  { key: 'groupaddr', icon: 'sitemap',  en: 'Group addresses',de: 'Gruppenadressen' },
  { key: 'settings',  icon: 'settings', en: 'Settings',       de: 'Einstellungen' },
];

/* ----------------------------------------------------------------- LOGIN */
function LoginScreen({ onLogin }) {
  const [user, setUser] = uS('demo');
  const [pw, setPw] = uS('');
  const [show, setShow] = uS(false);
  return (
    <div className="login-stage">
      <div className="login-card">
        <div className="login-top">
          <div className="login-logo"><span className="lg-k">K</span><span className="lg-nx">NX</span><span className="lg-rest">-NG-Monitor</span></div>
          <div className="login-tag">Professional KNX Bus Monitoring</div>
        </div>
        <div className="login-body">
          <div className="login-field">
            <label className="login-lab">Username*</label>
            <div className="login-input"><Icon name="user" size={18} /><input value={user} onChange={e => setUser(e.target.value)} /></div>
          </div>
          <div className="login-field">
            <div className="login-input"><Icon name="lock" size={18} /><input type={show ? 'text' : 'password'} placeholder="Password*" value={pw} onChange={e => setPw(e.target.value)} />
              <button className="login-eye" onClick={() => setShow(s => !s)}><Icon name={show ? 'eye' : 'eyeOff'} size={18} /></button></div>
          </div>
          <button className="knx-btn knx-btn--primary login-btn" onClick={onLogin}>Sign in</button>
        </div>
        <div className="login-foot">Version 1.0.0</div>
      </div>
    </div>
  );
}

/* ----------------------------------------------------------------- SHELL */
function KNXApp({ vp, theme, setTheme }) {
  const [page, setPage] = uS('monitor');
  const [lang, setLang] = uS('en');
  const [density, setDensity] = uS('compact');
  const [detail, setDetail] = uS(null);
  const [chartGA, setChartGA] = uS(null);
  uE(() => setDetail(null), [page, vp]);
  const go = key => { setPage(key); };
  const onChart = idOrRow => { const id = typeof idOrRow === 'string' ? idOrRow : idOrRow.dst; setChartGA(id); setDetail(null); setPage('charts'); };
  const L = n => lang === 'de' ? n.de : n.en;

  return (
    <div className={`knx-app vp-${vp} theme-${theme}`}>
      <header className="app-header on-dark">
        <div className="brand">
          <span className="wm"><span className="k">KNX</span><span className="x">·NG</span></span>
          <span className="brand-sub">MONITOR</span>
        </div>
        {vp !== 'mobile' && (
          <nav className="top-nav">
            {NAV.map(n => (
              <button key={n.key} className={`nav-link ${page === n.key ? 'active' : ''}`} onClick={() => go(n.key)} title={L(n)}>
                <Icon name={n.icon} size={17} /> {vp === 'desktop' && <span>{L(n)}</span>}
              </button>
            ))}
          </nav>
        )}
        <div className="user">
          <span className="lang-seg">
            <button className={lang === 'en' ? 'on' : ''} onClick={() => setLang('en')}>EN</button>
            <button className={lang === 'de' ? 'on' : ''} onClick={() => setLang('de')}>DE</button>
          </span>
          <span className="user-chip"><Icon name="user" size={17} /> {vp !== 'mobile' && 'demo'}</span>
          <button className="knx-btn knx-btn--icon knx-btn--ghost"><Icon name="logout" size={18} /></button>
        </div>
      </header>

      <main className="app-main">
        {page === 'monitor' && <MonitorView vp={vp} density={density} setDetail={setDetail} onChart={onChart} />}
        {page === 'charts' && <ChartsView vp={vp} initialGA={chartGA} />}
        {page === 'statistics' && <StatisticsView vp={vp} />}
        {page === 'projects' && <ProjectsView vp={vp} />}
        {page === 'topology' && <TopologyView vp={vp} />}
        {page === 'groupaddr' && <GroupAddressesView vp={vp} onChart={onChart} />}
        {page === 'settings' && <SettingsView vp={vp} theme={theme} setTheme={setTheme} density={density} setDensity={setDensity} />}
      </main>

      {vp === 'mobile' && (
        <nav className="bottom-nav">
          {NAV.map(n => (
            <button key={n.key} className={page === n.key ? 'active' : ''} onClick={() => go(n.key)}>
              <Icon name={n.icon} size={20} /><span>{L(n)}</span>
            </button>
          ))}
        </nav>
      )}

      {detail && <DetailSheet row={detail} onClose={() => setDetail(null)} onChart={onChart} vp={vp} />}
    </div>
  );
}

/* ----------------------------------------------------------------- PREVIEW */
const VIEWPORTS = [
  { key: 'desktop', label: 'Desktop', icon: 'columns', w: 1440, h: 900 },
  { key: 'tablet', label: 'Tablet', icon: 'box', w: 834, h: 1040 },
  { key: 'mobile', label: 'Mobile', icon: 'monitor', w: 390, h: 844 },
];

function Preview() {
  const [vp, setVp] = uS('desktop');
  const [theme, setTheme] = uS('light');
  const [authed, setAuthed] = uS(true);
  const [scale, setScale] = uS(1);
  const stageRef = uR(null);
  const cfg = VIEWPORTS.find(v => v.key === vp);
  uE(() => {
    function fit() {
      const el = stageRef.current; if (!el) return;
      const pad = 44;
      const aw = el.clientWidth - pad, ah = el.clientHeight - pad;
      const border = vp === 'mobile' ? 16 : vp === 'tablet' ? 18 : 0;
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
          <span className="pv-title">KNX-NG-Monitor · Prototype</span>
          <a className="pv-doclink" href="KNX Design System.html"><Icon name="chevron" size={13} style={{ transform: 'rotate(180deg)' }} /> Design system</a>
        </div>
        <span className="knx-seg pv-vp">
          {VIEWPORTS.map(v => (
            <button key={v.key} className={vp === v.key ? 'is-active' : ''} onClick={() => setVp(v.key)}><Icon name={v.icon} size={15} /> {v.label}</button>
          ))}
        </span>
        <div className="pv-right">
          <button className={`pv-ghost ${!authed ? 'on' : ''}`} onClick={() => setAuthed(a => !a)} title="Login screen"><Icon name="lock" size={15} /></button>
          <button className="pv-ghost" onClick={() => setTheme(t => t === 'light' ? 'dark' : 'light')} title="Toggle theme"><Icon name={theme === 'light' ? 'moon' : 'sun'} size={16} /></button>
          <span className="pv-dim mono">{cfg.w}×{cfg.h} · {Math.round(scale * 100)}%</span>
        </div>
      </div>
      <div className="preview-stage knx-scroll" ref={stageRef}>
        <div className="device-scaler" style={{ width: cfg.w * scale, height: cfg.h * scale }}>
          <div className={`device-frame frame-${vp} theme-${theme}`} style={{ width: cfg.w, height: cfg.h, transform: `scale(${scale})`, transformOrigin: 'top left' }}>
            {authed ? <KNXApp vp={vp} theme={theme} setTheme={setTheme} /> : <div className={`knx-app vp-${vp} theme-${theme}`}><LoginScreen onLogin={() => setAuthed(true)} /></div>}
          </div>
        </div>
      </div>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<Preview />);
