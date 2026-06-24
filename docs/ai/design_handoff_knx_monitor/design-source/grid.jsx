/* Shared KNX telegram grid (table / reduced / mobile cards) + detail slide-over. */
const { useState, useEffect, useRef, useMemo } = React;

const ALL_COLUMNS = [
  { key: 'time',  label: 'Timestamp', cls: 'col-time', min: 168, sortable: true, locked: true },
  { key: 'src',   label: 'Source',    cls: 'col-addr', min: 78,  sortable: true },
  { key: 'dst',   label: 'Dest',      cls: 'col-addr', min: 84,  sortable: true },
  { key: 'name',  label: 'Name',      cls: 'col-name', min: 240, sortable: true, grow: true, locked: true },
  { key: 'dpt',   label: 'DPT',       cls: 'col-dpt',  min: 96,  sortable: true },
  { key: 'type',  label: 'Type',      cls: 'col-type', min: 96,  sortable: true },
  { key: 'raw',   label: 'Raw',       cls: 'col-raw',  min: 96,  sortable: false },
  { key: 'val',   label: 'Value',     cls: 'col-val',  min: 120, sortable: true, locked: true },
];

function valClass(kind) {
  return kind === 'on' ? 'val-on' : kind === 'off' ? 'val-off' : kind === 'text' ? 'val-text' : 'val-num';
}
function RoomName({ row }) {
  const clean = row.name.replace(/^(EG|OG|DG|UG|KG)\s*/, '');
  return <React.Fragment>{row.room && <span className="name-room">{row.room}</span>}{clean}</React.Fragment>;
}
function TypeTag({ type }) {
  return <span className={`knx-type knx-type--${type}`}><span className="dot"></span>{KNXData.TYPE_LABEL[type]}</span>;
}
function ValueCell({ row }) {
  return <span className={valClass(row.valKind)}>{row.value}{row.unit ? <span className="unit">{row.unit}</span> : null}</span>;
}

function Grid({ rows, layout, density, sort, onSort, visibleCols, useDateTime, onRowClick, newIds }) {
  const cols = ALL_COLUMNS.filter(c => visibleCols.includes(c.key));

  if (layout === 'cards') {
    return (
      <div className="knx-cards knx-scroll">
        {rows.map(r => (
          <div key={r.id} className={`knx-mcard ${newIds && newIds.has(r.id) ? 'is-new' : ''}`} onClick={() => onRowClick(r)}>
            <div className="mc-top">
              <span className="mc-name"><RoomName row={r} /></span>
              <span className={`mc-val ${valClass(r.valKind)}`}>{r.value}{r.unit ? <span className="mc-unit">{r.unit}</span> : null}</span>
            </div>
            <div className="mc-meta">
              <span>{useDateTime ? r.datetime : r.time}</span>
              <span className="sep"></span>
              <span>{r.dst}</span>
              <span className="sep"></span>
              <TypeTag type={r.type} />
            </div>
          </div>
        ))}
        {rows.length === 0 && <Empty />}
      </div>
    );
  }

  return (
    <div className="knx-grid-wrap knx-scroll">
      <table className={`knx-grid ${density === 'cozy' ? 'is-cozy' : ''}`}>
        <colgroup>{cols.map(c => <col key={c.key} style={{ minWidth: c.min, width: c.grow ? 'auto' : c.min }} />)}</colgroup>
        <thead>
          <tr>
            {cols.map(c => (
              <th key={c.key} className={c.cls} onClick={() => c.sortable && onSort(c.key)} style={{ cursor: c.sortable ? 'pointer' : 'default' }}>
                <span className="th-inner">
                  {c.label}
                  {sort.key === c.key && <Icon name={sort.dir === 'asc' ? 'arrowUp' : 'arrowDown'} size={13} className="sort-ind" />}
                </span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr key={r.id} className={newIds && newIds.has(r.id) ? 'is-new' : ''} onClick={() => onRowClick(r)} style={{ cursor: 'pointer' }}>
              {cols.map(c => <Cell key={c.key} col={c} row={r} useDateTime={useDateTime} />)}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && <Empty />}
    </div>
  );
}

function Cell({ col, row, useDateTime }) {
  switch (col.key) {
    case 'time': return <td className="col-time">{useDateTime ? row.datetime : row.time}</td>;
    case 'src':  return <td className="col-addr">{row.src}</td>;
    case 'dst':  return <td className="col-addr">{row.dst}</td>;
    case 'name': return <td className="col-name"><RoomName row={row} /></td>;
    case 'dpt':  return <td className="col-dpt">{row.dpt}</td>;
    case 'type': return <td className="col-type"><TypeTag type={row.type} /></td>;
    case 'raw':  return <td className="col-raw">{row.raw}</td>;
    case 'val':  return <td className="col-val"><ValueCell row={row} /></td>;
    default: return <td></td>;
  }
}

function Empty() {
  return (
    <div className="knx-empty">
      <Icon name="search" size={28} />
      <p>No telegrams match these filters.</p>
      <span>Adjust your search or reset the filters.</span>
    </div>
  );
}

/* Detail slide-over — right side on desktop/tablet, bottom sheet on mobile. */
function DetailSheet({ row, onClose, onChart, vp }) {
  const [val, setVal] = useState('');
  useEffect(() => {
    const h = e => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);
  if (!row) return null;
  const fields = [
    ['Timestamp', row.datetime],
    ['Source', `${row.src} · ${row.srcName}`],
    ['Dest GA', row.dst],
    ['DPT', row.dpt],
    ['Raw', row.raw],
    ['Type', KNXData.TYPE_LABEL[row.type]],
    ['Priority', row.priority],
    ['Flags', row.flags],
  ];
  return (
    <div className={`sheet-backdrop sheet-${vp === 'mobile' ? 'bottom' : 'right'}`} onClick={onClose}>
      <div className="sheet" onClick={e => e.stopPropagation()}>
        <div className="sheet-head">
          <div className="sheet-head-main">
            <div className="sheet-name"><RoomName row={row} /></div>
            <TypeTag type={row.type} />
          </div>
          <button className="knx-btn knx-btn--icon knx-btn--ghost" onClick={onClose}><Icon name="close" /></button>
        </div>
        <div className="sheet-value">
          <span className={valClass(row.valKind)}>{row.value}</span>
          {row.unit ? <span className="sheet-unit">{row.unit}</span> : null}
        </div>
        <dl className="sheet-fields">
          {fields.map(([k, v]) => (
            <div className="sheet-field" key={k}>
              <dt>{k}</dt><dd className="mono">{v}</dd>
            </div>
          ))}
        </dl>
        <div className="sheet-block">
          <div className="sheet-block-title">Bus actions</div>
          <div className="sheet-actions">
            <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={15} /> Read</button>
            <button className="knx-btn knx-btn--outline knx-btn--sm" onClick={() => onChart && onChart(row)}><Icon name="chart" size={15} /> Chart</button>
          </div>
          <div className="sheet-write">
            <input className="knx-input" placeholder="Value" value={val} onChange={e => setVal(e.target.value)} />
            <button className="knx-btn knx-btn--primary knx-btn--sm" disabled={!val}><Icon name="upload" size={15} /> Write</button>
          </div>
          <div className="sheet-note">Write sends a value to the live bus.</div>
        </div>
        <div className="sheet-block">
          <div className="sheet-block-title">Used by</div>
          <div className="used-by">
            <span className="ub-addr mono">{row.src}</span>
            <span className="ub-name">{row.srcName}</span>
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { Grid, DetailSheet, ALL_COLUMNS, TypeTag, RoomName, ValueCell, valClass });
