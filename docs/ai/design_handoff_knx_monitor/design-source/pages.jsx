/* Projects (ETS import, detail) & Settings pages. */
const { useState: uSp, useEffect: uEp } = React;

const PROJECTS_SEED = [
  { id: 1, name: 'myHome_ETS6_v3.knxproj', file: 'myHome_ETS6_v3.knxproj', date: '6/19/26, 7:27 AM', gas: 843, devices: 94, active: true, autoconnect: true },
];

function flattenGas() {
  const out = [];
  KNXData.GA_TREE.forEach(t => t.children.forEach(m => m.gas.forEach(g => out.push(g))));
  return out.sort((a, b) => a[0].localeCompare(b[0], undefined, { numeric: true }));
}

function ProjectsView({ vp }) {
  const [projects, setProjects] = uSp(PROJECTS_SEED);
  const [importing, setImporting] = uSp(false);
  const [detail, setDetail] = uSp(null);
  const patch = (id, p) => setProjects(ps => ps.map(x => x.id === id ? { ...x, ...p } : x));
  const setActive = id => setProjects(ps => ps.map(p => ({ ...p, active: p.id === id })));
  const remove = id => setProjects(ps => ps.filter(p => p.id !== id));
  const onImported = proj => setProjects(ps => [{ ...proj, id: Date.now(), active: ps.length === 0, autoconnect: false }, ...ps]);
  const cards = vp === 'mobile';

  return (
    <div className="page">
      <div className="toolbar">
        <div className="tb-left"><h2 className="page-title">Projects</h2><span className="knx-badge knx-badge--count">{projects.length}</span></div>
        <div className="tb-right">
          <button className="knx-btn knx-btn--primary" onClick={() => setImporting(true)}><Icon name="upload" size={16} /> {vp !== 'mobile' ? 'Import project' : 'Import'}</button>
        </div>
      </div>
      <div className="page-scroll">
        <div className="projects-body">
          {cards ? (
            <div className="proj-cards">
              {projects.map(p => (
                <div key={p.id} className={`proj-card ${p.active ? 'is-active-row' : ''}`}>
                  <div className="proj-card-top">
                    <div><div className="proj-name">{p.name}</div><div className="proj-file">{p.date}</div></div>
                    <button className="knx-btn knx-btn--icon knx-btn--danger knx-btn--sm" onClick={() => remove(p.id)}><Icon name="trash" size={15} /></button>
                  </div>
                  <div className="proj-card-meta">
                    <span className="proj-stat"><Icon name="share" size={15} /><b>{p.gas}</b> GAs</span>
                    <span className="proj-stat"><Icon name="monitor" size={15} /><b>{p.devices}</b> devices</span>
                    <button className="knx-btn knx-btn--outline knx-btn--sm" onClick={() => setDetail(p)} style={{ marginLeft: 'auto' }}><Icon name="eye" size={14} /> View</button>
                  </div>
                  <div className="proj-card-toggles">
                    <label className="knx-switch"><input type="checkbox" checked={p.active} onChange={() => setActive(p.id)} /><span className="track"></span> Active</label>
                    <label className="knx-switch"><input type="checkbox" checked={p.autoconnect} onChange={() => patch(p.id, { autoconnect: !p.autoconnect })} /><span className="track"></span> Auto-connect</label>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <table className="proj-table">
              <thead><tr>
                <th>Name</th><th>File</th><th>Import date</th><th>Statistics</th><th>Active</th><th style={{ textAlign: 'right' }}>Actions</th>
              </tr></thead>
              <tbody>
                {projects.map(p => (
                  <tr key={p.id} className={p.active ? 'is-active-row' : ''}>
                    <td><span className="proj-name">{p.name}</span></td>
                    <td><span className="proj-file">{p.file}</span></td>
                    <td><span className="proj-date mono">{p.date}</span></td>
                    <td><div className="proj-stats">
                      <span className="proj-stat"><Icon name="share" size={15} /><b>{p.gas}</b> GAs</span>
                      <span className="proj-stat"><Icon name="monitor" size={15} /><b>{p.devices}</b> devices</span>
                    </div></td>
                    <td><div className="proj-toggles">
                      <label className="knx-switch"><input type="checkbox" checked={p.active} onChange={() => setActive(p.id)} /><span className="track"></span> Active</label>
                      <label className="knx-switch"><input type="checkbox" checked={p.autoconnect} onChange={() => patch(p.id, { autoconnect: !p.autoconnect })} /><span className="track"></span> Auto-connect</label>
                    </div></td>
                    <td><div className="proj-actions">
                      <button className="knx-btn knx-btn--icon knx-btn--ghost knx-btn--sm" title="View" onClick={() => setDetail(p)}><Icon name="eye" size={15} /></button>
                      <button className="knx-btn knx-btn--icon knx-btn--ghost knx-btn--sm" title="Keyring"><Icon name="key" size={15} /></button>
                      <button className="knx-btn knx-btn--icon knx-btn--danger knx-btn--sm" title="Delete" onClick={() => remove(p.id)}><Icon name="trash" size={15} /></button>
                    </div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
      {importing && <ImportModal onClose={() => setImporting(false)} onImported={onImported} />}
      {detail && <ProjectDetail proj={detail} onClose={() => setDetail(null)} />}
    </div>
  );
}

function ProjectDetail({ proj, onClose }) {
  const [openGa, setOpenGa] = uSp(true);
  const [openDev, setOpenDev] = uSp(false);
  const gas = flattenGas();
  uEp(() => { const h = e => e.key === 'Escape' && onClose(); window.addEventListener('keydown', h); return () => window.removeEventListener('keydown', h); }, []);
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal modal--lg" onClick={e => e.stopPropagation()}>
        <div className="modal-head modal-head--row">
          <h3>{proj.name}</h3>
          <button className="knx-btn knx-btn--icon knx-btn--ghost" onClick={onClose}><Icon name="close" /></button>
        </div>
        <div className="modal-body modal-body--scroll">
          <div className="accordion">
            <button className="acc-head" onClick={() => setOpenGa(o => !o)}>
              <span className="acc-title"><Icon name="share" size={16} /> Group Addresses ({proj.gas})</span>
              <Icon name={openGa ? 'chevronDown' : 'chevron'} size={16} />
            </button>
            {openGa && (
              <table className="acc-table">
                <thead><tr><th>Address</th><th>Name</th><th style={{ textAlign: 'right' }}>DPT</th></tr></thead>
                <tbody>{gas.slice(0, 14).map(g => <tr key={g[0]}><td className="mono">{g[0]}</td><td>{g[1]}</td><td className="mono" style={{ textAlign: 'right' }}>{g[2]}</td></tr>)}</tbody>
              </table>
            )}
          </div>
          <div className="accordion">
            <button className="acc-head" onClick={() => setOpenDev(o => !o)}>
              <span className="acc-title"><Icon name="monitor" size={16} /> Devices ({proj.devices})</span>
              <Icon name={openDev ? 'chevronDown' : 'chevron'} size={16} />
            </button>
            {openDev && (
              <div className="acc-devs">
                {KNXData.TOPOLOGY.children[0].children[0].children[3].children[0].devices.map((d, i) => <div className="acc-dev" key={i}>{d}</div>)}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function ImportModal({ onClose, onImported }) {
  const [file, setFile] = uSp(null);
  const [progress, setProgress] = uSp(null);
  uEp(() => { const h = e => e.key === 'Escape' && onClose(); window.addEventListener('keydown', h); return () => window.removeEventListener('keydown', h); }, []);
  const pick = () => setFile({ name: 'new_project_v6.knxproj', gas: 520 + Math.floor(Math.random() * 700), devices: 60 + Math.floor(Math.random() * 120) });
  const start = () => {
    setProgress(0); let p = 0;
    const t = setInterval(() => {
      p += 9 + Math.random() * 16; setProgress(Math.min(100, p));
      if (p >= 100) { clearInterval(t); setTimeout(() => { onImported({ name: file.name, file: file.name, date: 'just now', gas: file.gas, devices: file.devices }); onClose(); }, 350); }
    }, 170);
  };
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-head"><h3>Import project</h3><p className="modal-sub">Select ETS project</p></div>
        <div className="modal-body">
          <p className="import-hint">Please choose a <code>.knxproj</code> file:</p>
          <button className={`choose-file ${file ? 'has-file' : ''}`} onClick={pick}>
            <Icon name={file ? 'checkCircle' : 'upload'} size={18} /> {file ? file.name : 'Choose file'}
          </button>
          {file && <div className="import-detected">{file.gas} GAs · {file.devices} devices detected</div>}
          {progress !== null && <div className="import-progress"><div style={{ width: progress + '%' }}></div></div>}
        </div>
        <div className="modal-foot">
          <button className="knx-btn knx-btn--ghost" onClick={onClose}>Cancel</button>
          <button className="knx-btn knx-btn--primary" disabled={!file || progress !== null} onClick={start}><Icon name="upload" size={15} /> Start import</button>
        </div>
      </div>
    </div>
  );
}

/* ================================================================ SETTINGS */
function SettingsView({ vp, theme, setTheme, density, setDensity }) {
  const [ip, setIp] = uSp('192.168.10.60');
  const [port, setPort] = uSp('3671');
  const [pa, setPa] = uSp('1.0.58');
  const [buffer, setBuffer] = uSp('1000000');
  const [retention, setRetention] = uSp('');
  const [archive, setArchive] = uSp(false);
  const [tested, setTested] = uSp(null);

  return (
    <div className="settings-scroll">
      <div className="settings-col">
        <div className="toolbar settings-toolbar"><h2 className="page-title">Settings</h2></div>

        <div className="set-card">
          <div className="set-head"><span className="set-ic"><Icon name="sun" size={18} /></span><div><h3>Appearance</h3><p>Theme and grid density</p></div></div>
          <div className="set-body">
            <div className="set-row">
              <div><div className="set-row-label">Color scheme</div><div className="set-row-sub">Light for daytime, Console for dark control rooms</div></div>
              <span className="knx-seg">
                <button className={theme === 'light' ? 'is-active' : ''} onClick={() => setTheme('light')}><Icon name="sun" size={15} /> Light</button>
                <button className={theme === 'dark' ? 'is-active' : ''} onClick={() => setTheme('dark')}><Icon name="moon" size={15} /> Console</button>
              </span>
            </div>
            <div className="set-row">
              <div><div className="set-row-label">Grid density</div><div className="set-row-sub">Row height for Live View and History</div></div>
              <span className="knx-seg">
                <button className={density === 'compact' ? 'is-active' : ''} onClick={() => setDensity('compact')}>Compact</button>
                <button className={density === 'cozy' ? 'is-active' : ''} onClick={() => setDensity('cozy')}>Cozy</button>
              </span>
            </div>
          </div>
        </div>

        <div className="set-card">
          <div className="set-head"><span className="set-ic"><Icon name="wifi" size={18} /></span><div><h3>KNX Gateway</h3><p>Connection to the KNX interface</p></div></div>
          <div className="set-body">
            <div className="set-grid">
              <div className="set-field"><label className="knx-label">IP address</label>
                <div className="field-prefix"><Icon name="globe" size={16} className="pfx" /><input className="knx-input" value={ip} onChange={e => setIp(e.target.value)} /></div>
                <span className="set-hint">IP address of your KNX interface</span></div>
              <div className="set-field"><label className="knx-label">Port</label>
                <div className="field-prefix"><Icon name="swap" size={16} className="pfx" /><input className="knx-input" value={port} onChange={e => setPort(e.target.value)} /></div>
                <span className="set-hint">Default: 3671 (KNXnet/IP)</span></div>
              <div className="set-field set-field--wide"><label className="knx-label">Physical address</label>
                <div className="field-prefix"><Icon name="plug" size={16} className="pfx" /><input className="knx-input" value={pa} onChange={e => setPa(e.target.value)} /></div>
                <span className="set-hint">Format: area.line.device (e.g. <code>1.0.58</code>)</span></div>
            </div>
            <div className="info-line"><Icon name="wifi" size={16} /><span><b>Connection:</b> IP Tunneling (UDP) · <b>Protocol:</b> KNXnet/IP</span></div>
          </div>
          <div className="set-actions">
            <button className="knx-btn knx-btn--primary"><Icon name="save" size={15} /> Save</button>
            <button className="knx-btn knx-btn--outline" onClick={() => { setTested('ok'); setTimeout(() => setTested(null), 2500); }}><Icon name="link" size={15} /> Test connection</button>
            {tested === 'ok' && <span className="knx-status knx-status--ok"><span className="led"></span> Reachable · 12 ms</span>}
            <span className="spacer"></span>
            <button className="knx-btn knx-btn--ghost"><Icon name="refresh" size={15} /> {vp !== 'mobile' && 'Reset'}</button>
          </div>
        </div>

        <div className="set-card">
          <div className="set-head"><span className="set-ic"><Icon name="database" size={18} /></span><div><h3>Data recording</h3><p>Ring buffer and long-term archive</p></div></div>
          <div className="set-body">
            <div className="set-grid">
              <div className="set-field"><label className="knx-label">Ring buffer size</label>
                <div className="field-prefix"><Icon name="cpu" size={16} className="pfx" /><input className="knx-input" value={buffer} onChange={e => setBuffer(e.target.value)} /></div>
                <span className="set-hint">Max telegrams in the live SQLite buffer</span></div>
              <div className="set-field"><label className="knx-label">Archive retention (days)</label>
                <div className="field-prefix"><Icon name="calendar" size={16} className="pfx" /><input className="knx-input" value={retention} placeholder="unlimited" onChange={e => setRetention(e.target.value)} /></div>
                <span className="set-hint">Empty = keep forever</span></div>
            </div>
            <div className="set-row" style={{ marginTop: 'var(--sp-3)' }}>
              <div><div className="set-row-label">Long-term archive (NDJSON + gzip)</div><div className="set-row-sub">Records every telegram in daily files under <code>data/archive/</code></div></div>
              <label className="knx-switch"><input type="checkbox" checked={archive} onChange={e => setArchive(e.target.checked)} /><span className="track"></span></label>
            </div>
          </div>
          <div className="set-actions"><button className="knx-btn knx-btn--primary"><Icon name="save" size={15} /> Save</button></div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ProjectsView, SettingsView });
