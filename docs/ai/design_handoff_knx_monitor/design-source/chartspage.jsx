/* Charts page (line chart per group address) + Statistics page (bar chart). */
const { useState: uSch, useEffect: uEch, useMemo: uMch, useRef: uRch } = React;

const CHART_RANGES = [
  { key: '1h', label: '1h' }, { key: '24h', label: '24h' }, { key: '7d', label: '7d' }, { key: '30d', label: '30d' },
];

function RangeControls({ range, setRange, vp, extra }) {
  return (
    <div className="chart-controls">
      <span className="knx-seg">
        {CHART_RANGES.map(r => <button key={r.key} className={range === r.key ? 'is-active' : ''} onClick={() => setRange(r.key)}>{r.label}</button>)}
      </span>
      {vp === 'desktop' && <span className="tb-divider"></span>}
      <span className="cc-field"><span className="cc-lab">From</span><input className="knx-input" type="datetime-local" /></span>
      <span className="cc-field"><span className="cc-lab">To</span><input className="knx-input" type="datetime-local" /></span>
      <button className="knx-btn knx-btn--ghost knx-btn--sm" disabled>Apply</button>
      {extra}
    </div>
  );
}

function ChartsView({ vp, initialGA }) {
  const gaList = KNXData.CHART_GAS;
  const [gaId, setGaId] = uSch(initialGA || gaList[0].id);
  const ga = gaList.find(g => g.id === gaId) || gaList[0];
  const [range, setRange] = uSch('24h');
  const [live, setLive] = uSch(true);
  const [zoom, setZoom] = uSch([0, 1]);
  const [seed, setSeed] = uSch(0);
  // generate (and keep) a series per GA id
  const cacheRef = uRch({});
  const [data, setData] = uSch([]);
  uEch(() => {
    if (!cacheRef.current[gaId]) cacheRef.current[gaId] = ga.series();
    setData(cacheRef.current[gaId]);
    setZoom([0, 1]);
  }, [gaId, seed]);
  // live append
  uEch(() => {
    if (!live) return;
    const id = setInterval(() => {
      setData(prev => {
        if (!prev.length) return prev;
        const last = prev[prev.length - 1];
        const next = [last[0] + 3 * 60000, Math.max(0, last[1] + (Math.random() - 0.5) * (ga.unit === '°C' ? 0.6 : 180))];
        const out = [...prev.slice(1), [next[0], ga.unit === '°C' ? +next[1].toFixed(1) : Math.round(next[1])]];
        cacheRef.current[gaId] = out;
        return out;
      });
    }, 1600);
    return () => clearInterval(id);
  }, [live, gaId]);

  return (
    <div className="page chart-page">
      <div className="toolbar chart-toolbar">
        <div className="ga-select">
          <select className="knx-input" value={gaId} onChange={e => setGaId(e.target.value)}>
            {gaList.map(g => <option key={g.id} value={g.id}>{g.id} — {g.name}</option>)}
          </select>
        </div>
        <RangeControls range={range} setRange={setRange} vp={vp} extra={<React.Fragment>
          {vp === 'desktop' && <span className="tb-divider"></span>}
          <button className={`knx-btn knx-btn--outline knx-btn--sm ${live ? 'is-on' : ''}`} onClick={() => setLive(l => !l)}><Icon name="broadcast" size={15} /> Live</button>
          <button className="knx-btn knx-btn--outline knx-btn--sm" onClick={() => setSeed(s => s + 1)}><Icon name="refresh" size={15} /> Refresh</button>
        </React.Fragment>} />
      </div>
      <div className="chart-banner"><Icon name="info" size={15} /> Some series were down-sampled to keep the chart responsive.</div>
      <div className="page-scroll chart-scroll">
        <div className="chart-legend"><span className="legend-mark"></span> {ga.name}</div>
        {data.length > 0 && <LineChart data={data} unit={ga.unit} height={vp === 'mobile' ? 300 : 420} range={zoom} />}
        {data.length > 0 && <Brush data={data} range={zoom} onRange={setZoom} kind="line" />}
      </div>
    </div>
  );
}

function StatisticsView({ vp }) {
  const [range, setRange] = uSch('24h');
  const [zoom, setZoom] = uSch([0, 1]);
  const [bars] = uSch(() => KNXData.statsBars());
  return (
    <div className="page chart-page">
      <div className="toolbar chart-toolbar">
        <h2 className="page-title">Statistics</h2>
        <RangeControls range={range} setRange={setRange} vp={vp} />
      </div>
      <div className="page-scroll stats-scroll">
        <div className="stat-cards">
          <div className="stat-card">
            <div className="stat-label">Total telegrams</div>
            <div className="stat-value mono">{KNXData.TOTAL_24H.toLocaleString('en-US')}</div>
          </div>
          <div className="stat-card">
            <div className="stat-label">Average msg/s</div>
            <div className="stat-value mono">{KNXData.AVG_MSGS.toFixed(2)}</div>
          </div>
        </div>
        <div className="stat-chart-card">
          <div className="stat-chart-head">Telegrams over time</div>
          <BarChart data={bars} unit="telegrams" height={vp === 'mobile' ? 300 : 440} range={zoom} />
          <Brush data={bars} range={zoom} onRange={setZoom} kind="bar" />
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ChartsView, StatisticsView });
