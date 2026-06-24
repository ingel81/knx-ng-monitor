/* Lightweight SVG charting: line + bar charts, axes, hover tooltip, brush/zoom.
   No external deps. Used by Charts and Statistics pages. */
const { useState: uSc, useEffect: uEc, useRef: uRc, useMemo: uMc } = React;

const SERIES = '#4f63d6';

function useWidth(min) {
  const ref = uRc(null);
  const [w, setW] = uSc(min || 720);
  uEc(() => {
    const el = ref.current; if (!el) return;
    const ro = new ResizeObserver(es => setW(Math.max(40, es[0].contentRect.width)));
    ro.observe(el); return () => ro.disconnect();
  }, []);
  return [ref, w];
}

/* ---- nice axis scale ---- */
function niceNum(range, round) {
  const exp = Math.floor(Math.log10(range || 1));
  const f = (range || 1) / Math.pow(10, exp);
  let nf;
  if (round) nf = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
  else nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
  return nf * Math.pow(10, exp);
}
function niceScale(min, max, count) {
  if (min === max) { min -= 1; max += 1; }
  const range = niceNum(max - min, false);
  const step = niceNum(range / ((count || 6) - 1), true);
  const nmin = Math.floor(min / step) * step;
  const nmax = Math.ceil(max / step) * step;
  const ticks = [];
  for (let v = nmin; v <= nmax + step * 1e-6; v += step) ticks.push(+v.toFixed(6));
  return { min: nmin, max: nmax, step, ticks };
}
function fmtClock(t) { const d = new Date(t); const p = n => String(n).padStart(2, '0'); return `${p(d.getHours())}:${p(d.getMinutes())}`; }
function fmtNum(v) { return Math.abs(v) >= 1000 ? v.toLocaleString('en-US') : (Number.isInteger(v) ? v : v.toFixed(1)); }
function fmtFull(t) { const d = new Date(t); const p = n => String(n).padStart(2, '0'); return `${p(d.getDate())}/${p(d.getMonth() + 1)} ${p(d.getHours())}:${p(d.getMinutes())}`; }

/* x ticks: ~ one per ~110px, mark midnight with bold day number */
function xTicks(data, width, every) {
  if (!data.length) return [];
  const t0 = data[0][0], t1 = data[data.length - 1][0];
  const n = Math.max(2, Math.round(width / 130));
  const out = [];
  for (let i = 0; i <= n; i++) {
    const t = t0 + (t1 - t0) * (i / n);
    const d = new Date(t);
    const day = d.getHours() === 0;
    out.push({ t, label: day ? String(d.getDate()) : fmtClock(Math.round(t / 60000) * 60000), day });
  }
  return out;
}

/* =============================================================== LINE CHART */
function LineChart({ data, unit, height, range, color }) {
  const [ref, w] = useWidth();
  const [hover, setHover] = uSc(null);
  const H = height || 360;
  const padL = 56, padR = 16, padT = 24, padB = 26;
  const lo = range ? range[0] : 0, hi = range ? range[1] : 1;
  const view = uMc(() => {
    const a = Math.floor(lo * (data.length - 1)), b = Math.ceil(hi * (data.length - 1));
    return data.slice(a, b + 1);
  }, [data, lo, hi]);
  const ys = view.map(d => d[1]);
  const sc = niceScale(Math.min(...ys), Math.max(...ys), 7);
  const plotW = Math.max(10, w - padL - padR), plotH = H - padT - padB;
  const t0 = view[0][0], t1 = view[view.length - 1][0];
  const X = t => padL + (t1 === t0 ? 0 : (t - t0) / (t1 - t0)) * plotW;
  const Y = v => padT + (1 - (v - sc.min) / (sc.max - sc.min)) * plotH;
  const path = view.map((d, i) => `${i ? 'L' : 'M'}${X(d[0]).toFixed(1)} ${Y(d[1]).toFixed(1)}`).join(' ');
  const baseY = padT + plotH;
  const areaPath = `${path} L${X(t1).toFixed(1)} ${baseY} L${X(t0).toFixed(1)} ${baseY} Z`;
  const ticks = xTicks(view, plotW);
  const col = color;

  function onMove(e) {
    const rc = e.currentTarget.getBoundingClientRect();
    const px = e.clientX - rc.left;
    if (px < padL || px > w - padR) { setHover(null); return; }
    const frac = (px - padL) / plotW;
    const idx = Math.max(0, Math.min(view.length - 1, Math.round(frac * (view.length - 1))));
    setHover({ idx, d: view[idx] });
  }
  return (
    <div className="chart-host" ref={ref}>
      <svg className="chart-svg" width={w} height={H} onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
        <defs>
          <linearGradient id="series-grad" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="var(--series)" stopOpacity="0.20" />
            <stop offset="100%" stopColor="var(--series)" stopOpacity="0" />
          </linearGradient>
        </defs>
        {sc.ticks.map((v, i) => (
          <g key={i}>
            <line className="grid-line" x1={padL} y1={Y(v)} x2={w - padR} y2={Y(v)} />
            <text className="ax-y" x={padL - 10} y={Y(v) + 4}>{fmtNum(v)}</text>
          </g>
        ))}
        <text className="ax-unit" x={8} y={13}>{unit}</text>
        {ticks.map((tk, i) => (
          <text key={i} className={`ax-x ${tk.day ? 'is-day' : ''}`} x={X(tk.t)} y={H - 7}>{tk.label}</text>
        ))}
        <line className="ax-base" x1={padL} y1={padT + plotH} x2={w - padR} y2={padT + plotH} />
        <path className="series-fill" d={areaPath} fill="url(#series-grad)" />
        <path className="series-line" d={path} stroke={col} />
        {hover && (
          <g>
            <line className="cross" x1={X(hover.d[0])} y1={padT} x2={X(hover.d[0])} y2={padT + plotH} />
            <circle className="cross-dot" cx={X(hover.d[0])} cy={Y(hover.d[1])} r="4" stroke={col} />
          </g>
        )}
      </svg>
      {hover && (
        <div className="chart-tip" style={{ left: Math.min(w - 150, Math.max(0, X(hover.d[0]) + 12)), top: padT + 8 }}>
          <div className="tip-val" style={col ? { color: col } : null}>{fmtNum(hover.d[1])}<span className="tip-unit">{unit}</span></div>
          <div className="tip-time">{fmtFull(hover.d[0])}</div>
        </div>
      )}
    </div>
  );
}

/* =============================================================== BAR CHART */
function BarChart({ data, unit, height, range, color }) {
  const [ref, w] = useWidth();
  const [hover, setHover] = uSc(null);
  const H = height || 360;
  const padL = 56, padR = 16, padT = 24, padB = 26;
  const lo = range ? range[0] : 0, hi = range ? range[1] : 1;
  const view = uMc(() => {
    const a = Math.floor(lo * (data.length - 1)), b = Math.ceil(hi * (data.length - 1));
    return data.slice(a, b + 1);
  }, [data, lo, hi]);
  const sc = niceScale(0, Math.max(...view.map(d => d[1])), 7);
  const plotW = Math.max(10, w - padL - padR), plotH = H - padT - padB;
  const Y = v => padT + (1 - (v - sc.min) / (sc.max - sc.min)) * plotH;
  const bw = plotW / view.length;
  const ticks = xTicks(view, plotW);
  const t0 = view[0][0], t1 = view[view.length - 1][0];
  const X = t => padL + (t1 === t0 ? 0 : (t - t0) / (t1 - t0)) * plotW;
  const col = color;
  return (
    <div className="chart-host" ref={ref}>
      <svg className="chart-svg" width={w} height={H} onMouseLeave={() => setHover(null)}>
        {sc.ticks.map((v, i) => (
          <g key={i}>
            <line className="grid-line" x1={padL} y1={Y(v)} x2={w - padR} y2={Y(v)} />
            <text className="ax-y" x={padL - 10} y={Y(v) + 4}>{fmtNum(v)}</text>
          </g>
        ))}
        <text className="ax-unit" x={8} y={13}>{unit}</text>
        {view.map((d, i) => {
          const x = padL + i * bw, y = Y(d[1]);
          return <rect key={i} className="series-bar" x={x + bw * 0.16} y={y} width={bw * 0.68} height={padT + plotH - y}
            fill={col || undefined} onMouseEnter={() => setHover({ d, x: x + bw / 2, y })} />;
        })}
        {ticks.map((tk, i) => (
          <text key={i} className={`ax-x ${tk.day ? 'is-day' : ''}`} x={X(tk.t)} y={H - 7}>{tk.label}</text>
        ))}
        <line className="ax-base" x1={padL} y1={padT + plotH} x2={w - padR} y2={padT + plotH} />
      </svg>
      {hover && (
        <div className="chart-tip" style={{ left: Math.min(w - 150, Math.max(0, hover.x + 10)), top: padT + 8 }}>
          <div className="tip-val" style={col ? { color: col } : null}>{fmtNum(hover.d[1])}<span className="tip-unit">{unit}</span></div>
          <div className="tip-time">{fmtFull(hover.d[0])}</div>
        </div>
      )}
    </div>
  );
}

/* =============================================================== BRUSH / dataZoom */
function Brush({ data, range, onRange, kind, color }) {
  const [ref, w] = useWidth();
  const H = 56, padL = 56, padR = 16;
  const plotW = Math.max(10, w - padL - padR);
  const ys = data.map(d => d[1]);
  const mn = Math.min(...ys, 0), mx = Math.max(...ys);
  const Y = v => 8 + (1 - (v - mn) / (mx - mn || 1)) * (H - 16);
  const X = i => padL + (i / (data.length - 1)) * plotW;
  const area = data.map((d, i) => `${i ? 'L' : 'M'}${X(i).toFixed(1)} ${Y(d[1]).toFixed(1)}`).join(' ')
    + ` L${X(data.length - 1).toFixed(1)} ${H - 8} L${padL} ${H - 8} Z`;
  const [lo, hi] = range;
  const xL = padL + lo * plotW, xH = padL + hi * plotW;

  const drag = uRc(null);
  function pos(e) { const rc = ref.current.getBoundingClientRect(); return Math.max(0, Math.min(1, (e.clientX - rc.left - padL) / plotW)); }
  function down(mode) { return e => { e.preventDefault(); drag.current = { mode, start: pos(e), lo, hi }; }; }
  uEc(() => {
    function move(e) {
      if (!drag.current) return;
      const p = pos(e), d = drag.current, dd = p - d.start;
      if (d.mode === 'lo') onRange([Math.min(p, d.hi - 0.03), d.hi]);
      else if (d.mode === 'hi') onRange([d.lo, Math.max(p, d.lo + 0.03)]);
      else { const wd = d.hi - d.lo; let nl = Math.max(0, Math.min(1 - wd, d.lo + dd)); onRange([nl, nl + wd]); }
    }
    function up() { drag.current = null; }
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', up);
    return () => { window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up); };
  }, [onRange]);

  return (
    <div className="brush-host" ref={ref}>
      <svg className="brush-svg" width={w} height={H}>
        <path className="brush-area" d={area} />
        <rect className="brush-mask" x={padL} y="0" width={xL - padL} height={H} />
        <rect className="brush-mask" x={xH} y="0" width={w - padR - xH} height={H} />
        <rect className="brush-window" x={xL} y="0" width={xH - xL} height={H} onMouseDown={down('pan')} />
        <g className="brush-handle" onMouseDown={down('lo')}>
          <rect x={xL - 5} y={H / 2 - 12} width="10" height="24" rx="2" />
          <line x1={xL - 1.5} y1={H / 2 - 6} x2={xL - 1.5} y2={H / 2 + 6} /><line x1={xL + 1.5} y1={H / 2 - 6} x2={xL + 1.5} y2={H / 2 + 6} />
        </g>
        <g className="brush-handle" onMouseDown={down('hi')}>
          <rect x={xH - 5} y={H / 2 - 12} width="10" height="24" rx="2" />
          <line x1={xH - 1.5} y1={H / 2 - 6} x2={xH - 1.5} y2={H / 2 + 6} /><line x1={xH + 1.5} y1={H / 2 - 6} x2={xH + 1.5} y2={H / 2 + 6} />
        </g>
      </svg>
    </div>
  );
}

Object.assign(window, { LineChart, BarChart, Brush, SERIES });
