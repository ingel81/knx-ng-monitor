/* Shared KNX Grid: one component, used by Live View and History.
   Renders as full table / reduced table / mobile cards based on `layout`. */
const { useState, useEffect, useRef, useMemo } = React;

const ALL_COLUMNS = [
  { key: 'time',  label: 'Zeit',    cls: 'col-time', min: 96,  sortable: true },
  { key: 'src',   label: 'Quelle',  cls: 'col-addr', min: 70,  sortable: true },
  { key: 'dst',   label: 'Ziel',    cls: 'col-addr', min: 80,  sortable: true },
  { key: 'name',  label: 'Name',    cls: 'col-name', min: 220, sortable: true, grow: true },
  { key: 'dpt',   label: 'DPT',     cls: 'col-dpt',  min: 90,  sortable: true },
  { key: 'type',  label: 'Typ',     cls: '',         min: 96,  sortable: true },
  { key: 'raw',   label: 'Rohwert', cls: 'col-raw',  min: 90,  sortable: false },
  { key: 'val',   label: 'Wert',    cls: 'col-val',  min: 110, sortable: true },
];

function valClass(kind) {
  return kind === 'on' ? 'val-on' : kind === 'off' ? 'val-off' : kind === 'text' ? 'val-text' : 'val-num';
}
function RoomName({ row }) {
  if (!row.room) return row.name;
  return <React.Fragment><span className="name-room">{row.room}</span>{row.name.replace(/^(EG|OG|DG|UG|KG)\s*/, '')}</React.Fragment>;
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
          <div key={r.id} className="knx-mcard" onClick={() => onRowClick(r)}>
            <div className="mc-top">
              <span className="mc-name"><RoomName row={r} /></span>
              <span className={`mc-val ${valClass(r.valKind)}`}>{r.value}{r.unit ? <span style={{color:'var(--ink-3)',fontWeight:400,fontSize:'0.7em',marginLeft:3}}>{r.unit}</span> : null}</span>
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
              <th key={c.key} onClick={() => c.sortable && onSort(c.key)} style={{ cursor: c.sortable ? 'pointer' : 'default' }}>
                <span className="th-inner">
                  {c.key === 'time' && useDateTime ? 'Zeitpunkt' : c.label}
                  {sort.key === c.key && <Icon name={sort.dir === 'asc' ? 'arrowUp' : 'arrowDown'} size={13} className="sort-ind" />}
                </span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr key={r.id} className={newIds && newIds.has(r.id) ? 'is-new' : ''} onClick={() => onRowClick(r)} style={{cursor:'pointer'}}>
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
    case 'type': return <td><TypeTag type={row.type} /></td>;
    case 'raw':  return <td className="col-raw">{row.raw}</td>;
    case 'val':  return <td className="col-val"><ValueCell row={row} /></td>;
    default: return <td></td>;
  }
}

function Empty() {
  return (
    <div className="knx-empty">
      <Icon name="search" size={28} />
      <p>Keine Telegramme für diese Filter.</p>
      <span>Suche anpassen oder Filter zurücksetzen.</span>
    </div>
  );
}

/* Detail sheet — opens from bottom on mobile, side on desktop */
function DetailSheet({ row, onClose, useDateTime }) {
  useEffect(() => {
    const h = e => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);
  if (!row) return null;
  const fields = [
    ['Zeitpunkt', row.datetime, true],
    ['Quelle', row.src, true],
    ['Ziel-GA', row.dst, true],
    ['DPT', row.dpt, true],
    ['Rohwert', row.raw, true],
  ];
  return (
    <div className="sheet-backdrop" onClick={onClose}>
      <div className="sheet" onClick={e => e.stopPropagation()}>
        <div className="sheet-handle"></div>
        <div className="sheet-head">
          <div>
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
      </div>
    </div>
  );
}

Object.assign(window, { Grid, DetailSheet, ALL_COLUMNS });
