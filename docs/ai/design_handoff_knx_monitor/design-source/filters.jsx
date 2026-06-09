/* Unified filter toolbar: global search, quick chips, time range, column manager */
const { useState: useStateF, useRef: useRefF, useEffect: useEffectF } = React;

const TIME_RANGES = [
  { key: 'all', label: 'Alle' },
  { key: '1h', label: 'Letzte Stunde' },
  { key: '24h', label: 'Heute' },
  { key: '7d', label: '7 Tage' },
];
const QUICK_TYPES = [
  { key: 'write', label: 'Write', color: 'var(--write)' },
  { key: 'read', label: 'Read', color: 'var(--read)' },
  { key: 'response', label: 'Response', color: 'var(--response)' },
];
const QUICK_TOPICS = [
  { key: 'temp', label: 'Temperatur', match: /temp|°c|taupunkt/i },
  { key: 'power', label: 'Leistung', match: /leistung|zähler|wh|\bw\b/i },
  { key: 'light', label: 'Licht', match: /licht|dimm|hellig/i },
  { key: 'shade', label: 'Beschattung', match: /jalousie|status|wind|west|süd|ost/i },
];

function SearchField({ value, onChange, placeholder }) {
  return (
    <div className="knx-field" style={{ flex: 1, minWidth: 180 }}>
      <Icon name="search" size={17} className="field-icon" />
      <input className="knx-input knx-input--search" value={value} placeholder={placeholder || 'Suche über alle Spalten…'}
        onChange={e => onChange(e.target.value)} />
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
  const lockable = { name: true, val: true }; // can't hide essentials
  return (
    <div className="popover-host" ref={ref}>
      <button className={`knx-btn knx-btn--outline knx-btn--sm ${open ? 'is-open' : ''}`} onClick={() => setOpen(o => !o)}>
        <Icon name="columns" size={15} /> Spalten
      </button>
      {open && (
        <div className="popover">
          <div className="popover-title">Spalten anzeigen</div>
          {order.map(c => (
            <label key={c.key} className={`col-opt ${lockable[c.key] ? 'is-locked' : ''}`}>
              <span className="col-check" data-on={visible.includes(c.key)}>
                {visible.includes(c.key) && <Icon name="check" size={12} />}
              </span>
              <input type="checkbox" checked={visible.includes(c.key)} disabled={lockable[c.key]}
                onChange={() => onToggle(c.key)} style={{ display: 'none' }} />
              <Icon name="drag" size={14} className="col-drag" />
              {c.label}
            </label>
          ))}
          <div className="popover-note">Name &amp; Wert sind immer sichtbar</div>
        </div>
      )}
    </div>
  );
}

/* The full toolbar shared by both pages */
function FilterBar({ q, setQ, time, setTime, types, toggleType, topics, toggleTopic,
                     visibleCols, toggleCol, onReset, activeCount, compact }) {
  return (
    <div className="filterbar">
      <div className="filterbar-main">
        <SearchField value={q} onChange={setQ} />
        <div className="fb-quick knx-scroll-x">
          {TIME_RANGES.map(t => (
            <button key={t.key} className={`knx-chip ${time === t.key ? 'is-active' : ''}`} onClick={() => setTime(t.key)}>
              {t.key === 'all' ? t.label : <React.Fragment><Icon name="clock" size={13} />{t.label}</React.Fragment>}
            </button>
          ))}
          <span className="fb-sep"></span>
          {QUICK_TYPES.map(t => (
            <button key={t.key} className={`knx-chip ${types.includes(t.key) ? 'is-active' : ''}`} onClick={() => toggleType(t.key)}>
              <span className="chip-dot" style={{ background: t.color, borderRadius: 2 }}></span>{t.label}
            </button>
          ))}
          <span className="fb-sep"></span>
          {QUICK_TOPICS.map(t => (
            <button key={t.key} className={`knx-chip ${topics.includes(t.key) ? 'is-active' : ''}`} onClick={() => toggleTopic(t.key)}>
              {t.label}
            </button>
          ))}
        </div>
        <ColumnManager visible={visibleCols} onToggle={toggleCol} order={ALL_COLUMNS} />
        {activeCount > 0 && (
          <button className="knx-btn knx-btn--ghost knx-btn--sm" onClick={onReset}>
            <Icon name="close" size={14} /> Zurücksetzen
          </button>
        )}
      </div>
    </div>
  );
}

Object.assign(window, { FilterBar, SearchField, ColumnManager, QUICK_TOPICS, QUICK_TYPES, TIME_RANGES });
