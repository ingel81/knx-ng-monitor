/* Filtering: live search, column manager, structured archive filter bar + logic. */
const { useState: useStateF, useRef: useRefF, useEffect: useEffectF } = React;

const TIME_RANGES = [
  { key: 'all', label: 'All' },
  { key: '1h', label: 'Last hour' },
  { key: 'today', label: 'Today' },
  { key: '7d', label: '7 days' },
];
const QUICK_TYPES = [
  { key: 'write', label: 'Write', color: 'var(--write)' },
  { key: 'read', label: 'Read', color: 'var(--read)' },
  { key: 'response', label: 'Response', color: 'var(--response)' },
];
const QUICK_TOPICS = [
  { key: 'temperature', label: 'Temperature' },
  { key: 'light', label: 'Light' },
  { key: 'shading', label: 'Shading' },
  { key: 'power', label: 'Power' },
];

function blankFilters() { return { q: '', time: 'all', types: [], topics: [], dst: '', src: '' }; }

function useFilters() {
  const [f, setF] = useStateF(blankFilters());
  const [visibleCols, setVisibleCols] = useStateF(ALL_COLUMNS.map(c => c.key));
  const set = patch => setF(s => ({ ...s, ...patch }));
  const toggle = (k, v) => setF(s => ({ ...s, [k]: s[k].includes(v) ? s[k].filter(x => x !== v) : [...s[k], v] }));
  const toggleCol = k => setVisibleCols(c => c.includes(k) ? c.filter(x => x !== k) : ALL_COLUMNS.filter(col => c.includes(col.key) || col.key === k).map(col => col.key));
  const reset = () => setF(blankFilters());
  const activeCount = (f.q ? 1 : 0) + (f.time !== 'all' ? 1 : 0) + f.types.length + f.topics.length + (f.dst ? 1 : 0) + (f.src ? 1 : 0);
  return { f, set, toggle, reset, visibleCols, toggleCol, activeCount };
}

function applyFilters(rows, f) {
  const now = Date.now();
  let span = null, from = null;
  if (f.time === '1h') span = 3600e3;
  else if (f.time === '7d') span = 7 * 86400e3;
  else if (f.time === 'today') { const d = new Date(); d.setHours(0, 0, 0, 0); from = d.getTime(); }
  const ql = f.q.trim().toLowerCase();
  const dst = f.dst.trim(), src = f.src.trim();
  return rows.filter(r => {
    if (span && now - r.ts > span) return false;
    if (from && r.ts < from) return false;
    if (f.types.length && !f.types.includes(r.type)) return false;
    if (f.topics.length && !f.topics.includes(r.topic)) return false;
    if (dst && !r.dst.includes(dst)) return false;
    if (src && !r.src.includes(src)) return false;
    if (ql) {
      const hay = `${r.datetime} ${r.src} ${r.srcName} ${r.dst} ${r.name} ${r.dpt} ${r.type} ${r.raw} ${r.value} ${r.unit}`.toLowerCase();
      if (!hay.includes(ql)) return false;
    }
    return true;
  });
}

function SearchField({ value, onChange, placeholder }) {
  return (
    <div className="knx-field knx-field--search">
      <Icon name="search" size={16} className="field-icon" />
      <input className="knx-input knx-input--search" value={value} placeholder={placeholder || 'Search…'} onChange={e => onChange(e.target.value)} />
      {value && <button className="field-clear" onClick={() => onChange('')}><Icon name="close" size={14} /></button>}
    </div>
  );
}

function ColumnManager({ visible, onToggle, order }) {
  const [open, setOpen] = useStateF(false);
  const ref = useRefF(null);
  useEffectF(() => {
    const h = e => ref.current && !ref.current.contains(e.target) && setOpen(false);
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, []);
  return (
    <div className="popover-host" ref={ref}>
      <button className={`knx-btn knx-btn--ghost knx-btn--sm ${open ? 'is-open' : ''}`} onClick={() => setOpen(o => !o)}>
        <Icon name="columns" size={15} /> Columns
      </button>
      {open && (
        <div className="popover">
          <div className="popover-title">Show columns</div>
          {order.map(c => (
            <label key={c.key} className={`col-opt ${c.locked ? 'is-locked' : ''}`}>
              <span className="col-check" data-on={visible.includes(c.key)}>
                {visible.includes(c.key) && <Icon name="check" size={12} />}
              </span>
              <input type="checkbox" checked={visible.includes(c.key)} disabled={c.locked} onChange={() => onToggle(c.key)} style={{ display: 'none' }} />
              {c.label}
            </label>
          ))}
          <div className="popover-note">Timestamp, Name &amp; Value stay visible</div>
        </div>
      )}
    </div>
  );
}

/* Structured archive filter bar — staged, applied via the Apply button. */
function ArchiveFilters({ draft, set, toggle, onApply, vp }) {
  return (
    <div className="archive-filters">
      <div className="af-row">
        <span className="af-label">Time</span>
        <div className="af-chips">
          {TIME_RANGES.map(t => (
            <button key={t.key} className={`knx-chip ${draft.time === t.key ? 'is-active' : ''}`} onClick={() => set({ time: t.key })}>{t.label}</button>
          ))}
        </div>
        {vp === 'desktop' && <span className="af-vsep"></span>}
        <span className="af-label">Type</span>
        <div className="af-chips">
          {QUICK_TYPES.map(t => (
            <button key={t.key} className={`knx-chip ${draft.types.includes(t.key) ? 'is-active' : ''}`} onClick={() => toggle('types', t.key)}>
              <span className="chip-dot" style={{ background: t.color }}></span>{t.label}
            </button>
          ))}
        </div>
        {vp === 'desktop' && <span className="af-vsep"></span>}
        <span className="af-label">Topic</span>
        <div className="af-chips">
          {QUICK_TOPICS.map(t => (
            <button key={t.key} className={`knx-chip ${draft.topics.includes(t.key) ? 'is-active' : ''}`} onClick={() => toggle('topics', t.key)}>{t.label}</button>
          ))}
        </div>
      </div>
      <div className="af-row af-inputs">
        <input className="knx-input" placeholder="Dest GA (1/2/3)" value={draft.dst} onChange={e => set({ dst: e.target.value })} />
        <input className="knx-input" placeholder="Source (1.1.5)" value={draft.src} onChange={e => set({ src: e.target.value })} />
        <input className="knx-input" type="datetime-local" />
        <input className="knx-input" type="datetime-local" />
        <button className="knx-btn knx-btn--primary" onClick={onApply}><Icon name="search" size={15} /> Apply</button>
      </div>
    </div>
  );
}

Object.assign(window, { useFilters, applyFilters, blankFilters, SearchField, ColumnManager, ArchiveFilters, QUICK_TOPICS, QUICK_TYPES, TIME_RANGES });
