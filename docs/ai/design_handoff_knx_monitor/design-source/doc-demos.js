/* Doc demos: icon set, grid preview, device mockups, scroll-spy nav */
(function () {
  const NS = 'http://www.w3.org/2000/svg';
  // --- icon set (stroke, 24 viewBox) ---
  const ICONS = {
    live: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z|c|circle:12,12,3',
    history: 'M3 3v5h5|M3.05 13A9 9 0 1 0 6 5.3L3 8',
    folder: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
    settings: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z|M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-2.82 1.17V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 8 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15H4.5a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 6 9.4l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 11 4.6V4.5a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 2.82 1.17l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 11h.1a2 2 0 0 1 0 4h-.1z',
    search: 'M21 21l-4-4|c|circle:11,11,7',
    filter: 'M3 4h18l-7 8v6l-4 2v-8z',
    download: 'M12 3v12M7 11l5 5 5-5M5 21h14',
    pause: 'M7 5v14M17 5v14',
    play: 'M7 4l13 8-13 8z',
    clear: 'M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6',
    columns: 'M3 5h18v14H3zM9 5v14M15 5v14',
    sort: 'M7 4v16M4 8l3-4 3 4M17 20V4M14 16l3 4 3-4',
    chevron: 'M9 18l6-6-6-6',
    close: 'M18 6L6 18M6 6l12 12',
    plus: 'M12 5v14M5 12h14',
    disconnect: 'M9 12l-3 3a3 3 0 0 1-4-4l3-3M15 12l3-3a3 3 0 0 0-4-4l-3 3M4 20L20 4',
    user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z|M4 21a8 8 0 0 1 16 0',
    grid: 'M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z'
  };
  function makeIcon(name, size) {
    const def = ICONS[name]; if (!def) return null;
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor'); svg.setAttribute('stroke-width', '1.75');
    svg.setAttribute('stroke-linecap', 'round'); svg.setAttribute('stroke-linejoin', 'round');
    if (size) { svg.setAttribute('width', size); svg.setAttribute('height', size); }
    def.split('|').forEach(seg => {
      if (seg === 'c') return;
      if (seg.startsWith('circle:')) {
        const [cx, cy, r] = seg.slice(7).split(',');
        const c = document.createElementNS(NS, 'circle');
        c.setAttribute('cx', cx); c.setAttribute('cy', cy); c.setAttribute('r', r);
        svg.appendChild(c);
      } else {
        const p = document.createElementNS(NS, 'path');
        p.setAttribute('d', seg); svg.appendChild(p);
      }
    });
    return svg;
  }
  window.knxIcon = makeIcon;

  // --- render icon demo ---
  const iconDemo = document.getElementById('iconDemo');
  if (iconDemo) {
    Object.keys(ICONS).forEach(name => {
      const cell = document.createElement('div');
      cell.style.cssText = 'display:flex;flex-direction:column;align-items:center;gap:6px;width:64px;color:var(--ink-2)';
      const ic = makeIcon(name, 22); cell.appendChild(ic);
      const lbl = document.createElement('span');
      lbl.textContent = name; lbl.style.cssText = 'font-family:var(--font-mono);font-size:10px;color:var(--ink-3)';
      cell.appendChild(lbl); iconDemo.appendChild(cell);
    });
  }

  // --- sample rows for the grid demo ---
  const ROWS = [
    ['14:56:02,259', '1.1.9', '10/4/46', 'EG Küche Geschirrspüler Leistung', 'DPST-14-56', 'write', '4023D70A', '2,56', 'num', 'W'],
    ['14:56:01,878', '1.1.19', '0/1/1', 'Zähler Gesamt', 'DPST-13-10', 'write', '01C00F95', '29.364.117', 'num', 'Wh'],
    ['14:56:01,853', '1.1.9', '10/4/45', 'EG Küche Kühlschrank Leistung', 'DPST-14-56', 'write', '425B1EB8', '54,78', 'num', 'W'],
    ['14:56:00,900', '1.0.50', '10/3/210', 'Garage rechts Temp.', 'DPST-9-1', 'read', '0000', '0,00', 'num', '°C'],
    ['14:56:00,877', '1.0.40', '20/1/236', 'Garten Ventil 0 Haupt schalten', 'DPST-1-11', 'write', '00', 'Off', 'off', ''],
    ['14:55:59,026', '1.1.9', '10/4/76', 'EG Wohnen TV Steckd. 4 Leistung', 'DPST-14-56', 'write', '4194F5C3', '18,62', 'num', 'W'],
    ['14:55:58,858', '1.1.19', '0/1/0', 'Leistung aktuell', 'DPST-14-56', 'write', '44250CCD', '660,20', 'num', 'W'],
    ['14:55:58,786', '1.0.61', '0/2/9', 'Luftdruck', 'DPST-14-58', 'response', '47C67A00', '101.620,00', 'num', 'Pa'],
    ['14:55:57,780', '1.0.61', '0/2/4', 'Temperatur Nordseite', 'DPST-9-1', 'write', '0C15', '20,90', 'num', '°C'],
    ['14:55:51,626', '1.0.60', '6/0/3', 'West Status', 'DPST-1-1', 'write', '01', 'On', 'on', ''],
  ];
  const TYPE_LABEL = { write: 'Write', read: 'Read', response: 'Response', groupread: 'GroupRead' };

  function valCell(v, kind, unit) {
    const cls = kind === 'on' ? 'val-on' : kind === 'off' ? 'val-off' : kind === 'text' ? 'val-text' : 'val-num';
    return `<span class="${cls}">${v}${unit ? `<span class="unit">${unit}</span>` : ''}</span>`;
  }
  function roomTag(name) {
    const m = name.match(/^(EG|OG|DG|UG|KG)\b/);
    return m ? `<span class="name-room">${m[1]}</span>${name.slice(m[1].length).trim()}` : name;
  }

  function buildGrid(host, opts) {
    opts = opts || {};
    const cols = opts.cols || ['time', 'src', 'dst', 'name', 'dpt', 'type', 'raw', 'val'];
    const heads = { time: 'Zeit', src: 'Quelle', dst: 'Ziel', name: 'Name', dpt: 'DPT', type: 'Typ', raw: 'Rohwert', val: 'Wert' };
    let thead = '<tr>' + cols.map((c, i) =>
      `<th><span class="th-inner">${heads[c]}${i === 0 ? '' : ''}</span></th>`).join('') + '</tr>';
    let body = ROWS.slice(0, opts.limit || ROWS.length).map(r => {
      const [time, src, dst, name, dpt, type, raw, val, kind, unit] = r;
      const cellMap = {
        time: `<td class="col-time">${time}</td>`,
        src: `<td class="col-addr">${src}</td>`,
        dst: `<td class="col-addr">${dst}</td>`,
        name: `<td class="col-name">${roomTag(name)}</td>`,
        dpt: `<td class="col-dpt">${dpt}</td>`,
        type: `<td><span class="knx-type knx-type--${type}"><span class="dot"></span>${TYPE_LABEL[type]}</span></td>`,
        raw: `<td class="col-raw">${raw}</td>`,
        val: `<td class="col-val">${valCell(val, kind, unit)}</td>`
      };
      return '<tr>' + cols.map(c => cellMap[c]).join('') + '</tr>';
    }).join('');
    host.innerHTML = `<table class="knx-grid"><thead>${thead}</thead><tbody>${body}</tbody></table>`;
    // sort indicator on Zeit
    const firstTh = host.querySelector('thead th .th-inner');
    if (firstTh) { const s = makeIcon('chevron', 13); s.classList.add('sort-ind'); s.style.transform = 'rotate(90deg)'; firstTh.appendChild(s); }
  }

  const gd = document.getElementById('gridDemo');
  if (gd) buildGrid(gd, {});

  // --- device mockups ---
  function header(scale, live) {
    return `<div style="height:${28 * scale}px;background:var(--header-bg);display:flex;align-items:center;padding:0 ${10 * scale}px;gap:${5 * scale}px">
      <span style="color:var(--live);font-weight:700;font-size:${11 * scale}px">KNX</span><span style="color:#fff;font-weight:700;font-size:${11 * scale}px">·NG</span>
      ${live ? `<span style="margin-left:auto;width:${7 * scale}px;height:${7 * scale}px;border-radius:50%;background:var(--live)"></span>` : '<span style="margin-left:auto;color:var(--header-ink-2);font-size:' + (9 * scale) + 'px">⋯</span>'}
    </div>`;
  }
  const md = document.getElementById('mockDesktop');
  if (md) {
    md.innerHTML = header(1, true) +
      `<div style="background:var(--paper-sunk);height:26px;border-bottom:1px solid var(--line-2);display:flex;align-items:center;gap:6px;padding:0 10px">
        <div style="flex:1;height:16px;background:#fff;border:1px solid var(--line-strong);border-radius:4px"></div>
        <span style="font-size:9px;color:var(--teal-800);background:var(--brand-tint);padding:1px 6px;border-radius:8px">Heute</span>
       </div>`;
    const g = document.createElement('div'); md.appendChild(g);
    buildGrid(g, { limit: 7 });
    g.querySelector('table').style.fontSize = '10px';
  }
  const mt = document.getElementById('mockTablet');
  if (mt) {
    mt.style.height = '240px';
    mt.innerHTML = header(1, true) +
      `<div style="background:var(--paper-sunk);height:24px;display:flex;align-items:center;gap:5px;padding:0 8px;border-bottom:1px solid var(--line-2)">
        <div style="flex:1;height:15px;background:#fff;border:1px solid var(--line-strong);border-radius:4px"></div>
       </div>`;
    const g = document.createElement('div'); mt.appendChild(g);
    buildGrid(g, { cols: ['time', 'name', 'type', 'val'], limit: 7 });
    g.querySelector('table').style.fontSize = '9.5px';
  }
  const mp = document.getElementById('mockPhone');
  if (mp) {
    mp.style.height = '330px';
    let cards = ROWS.slice(0, 5).map(r => {
      const [time, src, dst, name, dpt, type, raw, val, kind, unit] = r;
      const cls = kind === 'on' ? 'val-on' : kind === 'off' ? 'val-off' : 'val-num';
      return `<div style="background:#fff;border:1px solid var(--line);border-radius:7px;padding:7px 9px;margin-bottom:6px">
        <div style="display:flex;justify-content:space-between;align-items:center;gap:6px">
          <span style="font-size:10px;font-weight:500;color:var(--ink);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${name}</span>
          <span class="mono ${cls}" style="font-size:12px;font-weight:600;white-space:nowrap">${val}${unit ? '<span style=\"color:var(--ink-3);font-size:8px\"> ' + unit + '</span>' : ''}</span>
        </div>
        <div class="mono" style="display:flex;gap:6px;margin-top:3px;font-size:8px;color:var(--ink-3)">
          <span>${time.slice(0, 8)}</span><span>${dst}</span><span class="knx-type knx-type--${type}" style="font-size:8px"><span class="dot" style="width:5px;height:5px"></span></span>
        </div>
      </div>`;
    }).join('');
    mp.innerHTML = header(1, true) +
      `<div style="padding:8px"><div style="height:26px;background:#fff;border:1px solid var(--line-strong);border-radius:6px;display:flex;align-items:center;padding:0 8px;margin-bottom:8px;gap:6px;color:var(--ink-3);font-size:9px">⌕ Suche…</div>${cards}</div>`;
  }

  // --- scroll-spy ---
  const links = [...document.querySelectorAll('#docNav a')].filter(a => a.getAttribute('href').startsWith('#'));
  const map = {}; links.forEach(a => map[a.getAttribute('href').slice(1)] = a);
  const obs = new IntersectionObserver(entries => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        links.forEach(l => l.classList.remove('active'));
        if (map[e.target.id]) map[e.target.id].classList.add('active');
      }
    });
  }, { rootMargin: '-20% 0px -70% 0px' });
  document.querySelectorAll('.sec[id]').forEach(s => obs.observe(s));
})();
