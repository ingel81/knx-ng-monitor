/* Monitor page: Live stream + Archive (historized) views. */
const { useState: uSm, useEffect: uEm, useMemo: uMm, useRef: uRm } = React;

function useLiveStream(running) {
  const [rows, setRows] = uSm(() => KNXData.buildHistory(40));
  const [newIds, setNewIds] = uSm(() => new Set());
  const [rate, setRate] = uSm(1.4);
  uEm(() => {
    if (!running) return;
    const id = setInterval(() => {
      const batch = Array.from({ length: 1 + Math.floor(Math.random() * 2) }, () => KNXData.makeTelegram());
      setRows(prev => [...batch.reverse(), ...prev].slice(0, 600));
      const ids = new Set(batch.map(b => b.id));
      setNewIds(ids);
      setRate(+(0.9 + Math.random() * 2.6).toFixed(1));
      setTimeout(() => setNewIds(new Set()), 600);
    }, 950);
    return () => clearInterval(id);
  }, [running]);
  return { rows, newIds, rate, clear: () => setRows([]) };
}

function layoutFor(vp) { return vp === 'mobile' ? 'cards' : 'table'; }
function colsFor(vp, visible) {
  if (vp === 'tablet') return visible.filter(k => ['time', 'src', 'dst', 'name', 'type', 'val'].includes(k));
  return visible;
}

function MonitorView({ vp, density, setDetail, onChart }) {
  const [mode, setMode] = uSm('live');
  const live = (
    <LiveBoard vp={vp} density={density} setDetail={setDetail} onChart={onChart} mode={mode} setMode={setMode} />
  );
  const archive = (
    <ArchiveBoard vp={vp} density={density} setDetail={setDetail} onChart={onChart} mode={mode} setMode={setMode} />
  );
  return mode === 'live' ? live : archive;
}

function ModeSwitch({ mode, setMode, paused, setPaused, autoscroll, setAutoscroll }) {
  return (
    <div className="mon-modes">
      <button className={`knx-btn knx-btn--outline knx-btn--sm ${mode === 'live' ? 'is-on' : ''}`} onClick={() => setMode('live')}><Icon name="eye" size={15} /> Live</button>
      <button className={`knx-btn knx-btn--outline knx-btn--sm ${mode === 'archive' ? 'is-on' : ''}`} onClick={() => setMode('archive')}><Icon name="history" size={15} /> Archive</button>
      {mode === 'live' && <button className="knx-btn knx-btn--outline knx-btn--sm" onClick={() => setPaused(p => !p)}><Icon name={paused ? 'play' : 'pause'} size={15} /> {paused ? 'Resume' : 'Pause'}</button>}
      {mode === 'live' && (
        <label className="knx-switch mon-autoscroll"><input type="checkbox" checked={autoscroll} onChange={e => setAutoscroll(e.target.checked)} /><span className="track"></span> Auto-scroll</label>
      )}
    </div>
  );
}

function LiveBoard({ vp, density, setDetail, onChart, mode, setMode }) {
  const [paused, setPaused] = uSm(false);
  const [autoscroll, setAutoscroll] = uSm(true);
  const [q, setQ] = uSm('');
  const [cols, setCols] = uSm(ALL_COLUMNS.map(c => c.key));
  const toggleCol = k => setCols(c => c.includes(k) ? c.filter(x => x !== k) : ALL_COLUMNS.filter(col => c.includes(col.key) || col.key === k).map(col => col.key));
  const { rows, newIds, rate, clear } = useLiveStream(!paused);
  const filtered = uMm(() => applyFilters(rows, { ...blankFilters(), q }), [rows, q]);
  const scrollRef = uRm(null);
  uEm(() => { if (autoscroll && !paused && scrollRef.current) scrollRef.current.scrollTop = 0; }, [filtered.length, autoscroll, paused]);

  return (
    <div className="page">
      <div className="toolbar mon-toolbar">
        <ModeSwitch mode={mode} setMode={setMode} paused={paused} setPaused={setPaused} autoscroll={autoscroll} setAutoscroll={setAutoscroll} />
        <div className="tb-right">
          <span className="knx-status knx-status--live" style={{ opacity: paused ? .45 : 1 }}><span className="led"></span> {paused ? 'Paused' : 'Live'} <span className="knx-badge knx-badge--count">{filtered.length}</span></span>
          {vp !== 'mobile' && <span className="rate-pill mono">{rate} msg/s · {Math.round(rate / 50 * 100)}%</span>}
          <span className="tb-divider"></span>
          {vp !== 'mobile' && <SearchField value={q} onChange={setQ} />}
          {vp === 'desktop' && <ColumnManager visible={cols} onToggle={toggleCol} order={ALL_COLUMNS} />}
          <button className="knx-btn knx-btn--ghost knx-btn--sm" onClick={clear}><Icon name="clear" size={15} /> {vp === 'desktop' && 'Clear'}</button>
          <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={15} /> {vp === 'desktop' && 'Export'}</button>
        </div>
      </div>
      {vp === 'mobile' && <div className="mon-msearch"><SearchField value={q} onChange={setQ} /></div>}
      <div className="grid-host" ref={scrollRef}>
        <Grid rows={filtered} layout={layoutFor(vp)} density={density} sort={{ key: 'time', dir: 'desc' }} onSort={() => {}}
          visibleCols={colsFor(vp, cols)} useDateTime={false} onRowClick={setDetail} newIds={newIds} />
      </div>
    </div>
  );
}

function ArchiveBoard({ vp, density, setDetail, onChart, mode, setMode }) {
  const [rows] = uSm(() => KNXData.buildHistory(700));
  const [draft, setDraft] = uSm(blankFilters);
  const [applied, setApplied] = uSm(blankFilters);
  const [q, setQ] = uSm('');
  const [cols, setCols] = uSm(ALL_COLUMNS.map(c => c.key));
  const [sort, setSort] = uSm({ key: 'time', dir: 'desc' });
  const toggleCol = k => setCols(c => c.includes(k) ? c.filter(x => x !== k) : ALL_COLUMNS.filter(col => c.includes(col.key) || col.key === k).map(col => col.key));
  const set = patch => setDraft(s => ({ ...s, ...patch }));
  const toggle = (k, v) => setDraft(s => ({ ...s, [k]: s[k].includes(v) ? s[k].filter(x => x !== v) : [...s[k], v] }));
  const onSort = key => setSort(s => s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' });

  const filtered = uMm(() => {
    let out = applyFilters(rows, { ...applied, q });
    const dir = sort.dir === 'asc' ? 1 : -1;
    out = [...out].sort((a, b) => {
      let av = a[sort.key], bv = b[sort.key];
      if (sort.key === 'time') { av = a.ts; bv = b.ts; }
      if (sort.key === 'val') { av = parseFloat(String(a.value).replace(/\./g, '').replace(',', '.')); bv = parseFloat(String(b.value).replace(/\./g, '').replace(',', '.')); if (isNaN(av)) av = -Infinity; if (isNaN(bv)) bv = -Infinity; }
      return av < bv ? -dir : av > bv ? dir : 0;
    });
    return out;
  }, [rows, applied, q, sort]);

  return (
    <div className="page">
      <div className="toolbar mon-toolbar">
        <ModeSwitch mode={mode} setMode={setMode} />
        <div className="tb-right">
          <span className="knx-badge knx-badge--count">{KNXData.TOTAL_TELEGRAMS.toLocaleString('en-US')}</span>
          {vp !== 'mobile' && <SearchField value={q} onChange={setQ} />}
          {vp === 'desktop' && <ColumnManager visible={cols} onToggle={toggleCol} order={ALL_COLUMNS} />}
          <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={15} /> {vp === 'desktop' && 'Export CSV'}</button>
          {vp === 'desktop' && <button className="knx-btn knx-btn--icon knx-btn--ghost knx-btn--sm"><Icon name="more" size={16} /></button>}
        </div>
      </div>
      <ArchiveFilters draft={draft} set={set} toggle={toggle} onApply={() => setApplied(draft)} vp={vp} />
      {vp === 'mobile' && <div className="mon-msearch"><SearchField value={q} onChange={setQ} /></div>}
      <div className="grid-host">
        <Grid rows={filtered} layout={layoutFor(vp)} density={density} sort={sort} onSort={onSort}
          visibleCols={colsFor(vp, cols)} useDateTime={true} onRowClick={setDetail} newIds={null} />
      </div>
    </div>
  );
}

Object.assign(window, { MonitorView });
