/* Projects & Settings pages — Instrument design language */
const { useState: uSp, useEffect: uEp } = React;

/* ---------------- sample projects ---------------- */
const PROJECTS = [
  { id: 1, name: 'myProject_ets_v5.7.7', file: 'myProject_ets_v5.7.7.knxproj', date: '02.11.25, 00:36', gas: 841, devices: 94, active: true },
  { id: 2, name: 'Bürogebäude Nordtrakt', file: 'buero_nord_v2.knxproj', date: '18.09.25, 14:02', gas: 1294, devices: 168, active: false },
  { id: 3, name: 'EFH Musterstraße', file: 'efh_muster.knxproj', date: '03.06.25, 09:21', gas: 312, devices: 41, active: false },
];

function ProjectsView({ vp }) {
  const [projects, setProjects] = uSp(PROJECTS);
  const [importing, setImporting] = uSp(false);
  const setActive = id => setProjects(ps => ps.map(p => ({ ...p, active: p.id === id })));
  const remove = id => setProjects(ps => ps.filter(p => p.id !== id));
  const onImported = proj => setProjects(ps => [{ ...proj, id: Date.now(), active: false }, ...ps]);
  const cards = vp === 'mobile';

  return (
    <div className="page">
      <div className="toolbar">
        <div className="tb-left">
          <h2 className="page-title">Projects</h2>
          <span className="knx-badge knx-badge--count">{projects.length}</span>
        </div>
        <div className="tb-right">
          <button className="knx-btn knx-btn--primary" onClick={() => setImporting(true)}>
            <Icon name="upload" size={16} /> {vp !== 'mobile' ? 'Projekt importieren' : 'Import'}
          </button>
        </div>
      </div>
      <div className="page-scroll">
        <div className="projects-body">
          {cards ? (
            <div className="proj-cards">
              {projects.map(p => (
                <div key={p.id} className={`proj-card ${p.active ? 'is-active-row' : ''}`}>
                  <div className="proj-card-top">
                    <div>
                      <div className="proj-name">{p.name}</div>
                      <div className="proj-file">{p.file}</div>
                    </div>
                    <label className="knx-switch"><input type="checkbox" checked={p.active} onChange={() => setActive(p.id)} /><span className="track"></span></label>
                  </div>
                  <div className="proj-card-meta">
                    <span className="proj-stat"><Icon name="sitemap" size={15} /><b>{p.gas}</b> GAs</span>
                    <span className="proj-stat"><Icon name="monitor" size={15} /><b>{p.devices}</b> Geräte</span>
                    <span className="proj-stat"><Icon name="calendar" size={15} />{p.date}</span>
                    <button className="knx-btn knx-btn--icon knx-btn--ghost knx-btn--sm" style={{ marginLeft: 'auto' }} onClick={() => remove(p.id)}><Icon name="trash" size={15} /></button>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <table className="proj-table">
              <thead>
                <tr>
                  <th>Name</th><th>Datei</th>{vp === 'desktop' && <th>Import-Datum</th>}<th>Statistik</th><th style={{ textAlign: 'center' }}>Aktiv</th><th style={{ textAlign: 'right' }}>Aktionen</th>
                </tr>
              </thead>
              <tbody>
                {projects.map(p => (
                  <tr key={p.id} className={p.active ? 'is-active-row' : ''}>
                    <td><span className="proj-name">{p.name}</span></td>
                    <td><span className="proj-file">{p.file}</span></td>
                    {vp === 'desktop' && <td><span className="proj-date">{p.date}</span></td>}
                    <td>
                      <div className="proj-stats">
                        <span className="proj-stat"><Icon name="sitemap" size={15} /><b>{p.gas}</b> GAs</span>
                        <span className="proj-stat"><Icon name="monitor" size={15} /><b>{p.devices}</b> Geräte</span>
                      </div>
                    </td>
                    <td style={{ textAlign: 'center' }}>
                      <label className="knx-switch" style={{ justifyContent: 'center' }}><input type="checkbox" checked={p.active} onChange={() => setActive(p.id)} /><span className="track"></span></label>
                    </td>
                    <td>
                      <div className="proj-actions">
                        <button className="knx-btn knx-btn--icon knx-btn--ghost knx-btn--sm" title="GAs anzeigen"><Icon name="eye" size={15} /></button>
                        <button className="knx-btn knx-btn--icon knx-btn--danger knx-btn--sm" title="Löschen" onClick={() => remove(p.id)}><Icon name="trash" size={15} /></button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
      {importing && <ImportModal onClose={() => setImporting(false)} onImported={onImported} />}
    </div>
  );
}

function ImportModal({ onClose, onImported }) {
  const [file, setFile] = uSp(null);
  const [progress, setProgress] = uSp(null);
  uEp(() => {
    const h = e => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h); return () => window.removeEventListener('keydown', h);
  }, []);
  const fakePick = () => setFile({ name: 'neues_projekt_v6.knxproj', gas: 520 + Math.floor(Math.random() * 700), devices: 60 + Math.floor(Math.random() * 120) });
  const start = () => {
    setProgress(0);
    let p = 0;
    const t = setInterval(() => {
      p += 8 + Math.random() * 14; setProgress(Math.min(100, p));
      if (p >= 100) { clearInterval(t); setTimeout(() => { onImported({ name: file.name.replace('.knxproj', ''), file: file.name, date: 'gerade eben', gas: file.gas, devices: file.devices }); onClose(); }, 350); }
    }, 180);
  };
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-head">
          <h3>Projekt importieren</h3>
          <p>ETS-Projekt (.knxproj) auswählen — daraus werden Gruppenadressen und Geräte gelesen.</p>
        </div>
        <div className="modal-body">
          <div className={`dropzone ${file ? 'has-file' : ''}`} onClick={fakePick}>
            <Icon name={file ? 'check' : 'upload'} size={26} />
            <div className="dz-main">{file ? file.name : 'Datei auswählen oder hierher ziehen'}</div>
            <div className="dz-sub">{file ? `${file.gas} GAs · ${file.devices} Geräte erkannt` : 'Nur .knxproj — max. 50 MB'}</div>
          </div>
          {progress !== null && <div className="import-progress"><div style={{ width: progress + '%' }}></div></div>}
        </div>
        <div className="modal-foot">
          <button className="knx-btn knx-btn--ghost" onClick={onClose}>Abbrechen</button>
          <button className="knx-btn knx-btn--primary" disabled={!file || progress !== null} onClick={start}>
            <Icon name="upload" size={15} /> Import starten
          </button>
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
        <div className="toolbar" style={{ background: 'transparent', borderBottom: 'none', padding: '0 0 var(--sp-2)' }}>
          <h2 className="page-title">Settings</h2>
        </div>

        {/* Appearance */}
        <div className="set-card">
          <div className="set-head">
            <span className="set-ic"><Icon name="sun" size={18} /></span>
            <div><h3>Darstellung</h3><p>Theme &amp; Standard-Dichte des Grids</p></div>
          </div>
          <div className="set-body">
            <div className="set-row">
              <div><div className="set-row-label">Farbschema</div><div className="set-row-sub">Light für Tageslicht, Console für Dauerbetrieb &amp; Nacht</div></div>
              <span className="knx-seg">
                <button className={theme === 'light' ? 'is-active' : ''} onClick={() => setTheme('light')}><Icon name="sun" size={15} /> Light</button>
                <button className={theme === 'dark' ? 'is-active' : ''} onClick={() => setTheme('dark')}><Icon name="moon" size={15} /> Console</button>
              </span>
            </div>
            <div className="set-row">
              <div><div className="set-row-label">Grid-Dichte</div><div className="set-row-sub">Wie viele Telegramme gleichzeitig sichtbar sind</div></div>
              <span className="knx-seg">
                <button className={density === 'compact' ? 'is-active' : ''} onClick={() => setDensity('compact')}>Dicht</button>
                <button className={density === 'cozy' ? 'is-active' : ''} onClick={() => setDensity('cozy')}>Komfort</button>
              </span>
            </div>
          </div>
        </div>

        {/* Gateway */}
        <div className="set-card">
          <div className="set-head">
            <span className="set-ic"><Icon name="wifi" size={18} /></span>
            <div><h3>KNX Gateway</h3><p>Verbindung zur KNXnet/IP-Schnittstelle</p></div>
          </div>
          <div className="set-body">
            <div className="set-grid">
              <div className="set-field">
                <label className="knx-label">IP-Adresse *</label>
                <div className="field-prefix"><Icon name="globe" size={16} className="pfx" /><input className="knx-input" value={ip} onChange={e => setIp(e.target.value)} /></div>
                <span className="set-hint">IP-Adresse deiner KNX-Schnittstelle</span>
              </div>
              <div className="set-field">
                <label className="knx-label">Port *</label>
                <div className="field-prefix"><Icon name="swap" size={16} className="pfx" /><input className="knx-input" value={port} onChange={e => setPort(e.target.value)} /></div>
                <span className="set-hint">Standard: 3671 (KNXnet/IP)</span>
              </div>
              <div className="set-field">
                <label className="knx-label">Physikalische Adresse *</label>
                <div className="field-prefix"><Icon name="plug" size={16} className="pfx" /><input className="knx-input" value={pa} onChange={e => setPa(e.target.value)} /></div>
                <span className="set-hint">Format: Bereich.Linie.Gerät (z.B. <code>1.0.58</code>)</span>
              </div>
            </div>
            <div className="info-line">
              <Icon name="live" size={16} />
              <span><b>Verbindung:</b> IP Tunneling (UDP) · <b>Protokoll:</b> KNXnet/IP</span>
            </div>
          </div>
          <div className="set-actions">
            <button className="knx-btn knx-btn--primary"><Icon name="save" size={15} /> Speichern</button>
            <button className="knx-btn knx-btn--outline" onClick={() => { setTested('ok'); setTimeout(() => setTested(null), 2500); }}>
              <Icon name="swap" size={15} /> Verbindung testen
            </button>
            {tested === 'ok' && <span className="knx-status knx-status--ok"><span className="led"></span> Erreichbar · 12 ms</span>}
            <span className="spacer"></span>
            <button className="knx-btn knx-btn--ghost"><Icon name="refresh" size={15} /> {vp !== 'mobile' && 'Zurücksetzen'}</button>
          </div>
        </div>

        {/* Data recording */}
        <div className="set-card">
          <div className="set-head">
            <span className="set-ic"><Icon name="database" size={18} /></span>
            <div><h3>Datenaufzeichnung</h3><p>Live-Puffer &amp; Langzeit-Archiv</p></div>
          </div>
          <div className="set-body">
            <div className="set-grid">
              <div className="set-field">
                <label className="knx-label">Ringpuffer-Größe</label>
                <div className="field-prefix"><Icon name="cpu" size={16} className="pfx" /><input className="knx-input" value={buffer} onChange={e => setBuffer(e.target.value)} /></div>
                <span className="set-hint">Max. Telegramme im Live-SQLite-Puffer</span>
              </div>
              <div className="set-field">
                <label className="knx-label">Archiv-Aufbewahrung (Tage)</label>
                <div className="field-prefix"><Icon name="calendar" size={16} className="pfx" /><input className="knx-input" value={retention} placeholder="—" onChange={e => setRetention(e.target.value)} /></div>
                <span className="set-hint">Leer = unbegrenzt aufbewahren</span>
              </div>
            </div>
            <div className="set-row" style={{ marginTop: 'var(--sp-3)' }}>
              <div><div className="set-row-label">Langzeit-Archiv (NDJSON + gzip)</div><div className="set-row-sub">Erfasst <b>jedes</b> Telegramm in Tagesdateien unter <code>data/archive/</code></div></div>
              <label className="knx-switch"><input type="checkbox" checked={archive} onChange={e => setArchive(e.target.checked)} /><span className="track"></span></label>
            </div>
          </div>
          <div className="set-actions">
            <button className="knx-btn knx-btn--primary"><Icon name="save" size={15} /> Speichern</button>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ProjectsView, SettingsView });
