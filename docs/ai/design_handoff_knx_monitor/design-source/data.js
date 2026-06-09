/* KNX sample telegram dataset + live generator */
(function () {
  // catalogue of realistic group objects: [name, dst, dpt, kind, unit, decode()]
  // kind: 'bool' | 'num' | 'text'
  const CATALOG = [
    ['EG Küche Geschirrspüler Leistung', '10/4/46', 'DPST-14-56', 'num', 'W', () => rnd(0, 2200, 2)],
    ['EG Küche Kühlschrank Leistung', '10/4/45', 'DPST-14-56', 'num', 'W', () => rnd(35, 95, 2)],
    ['EG Wohnen TV Steckd. 4 Leistung', '10/4/76', 'DPST-14-56', 'num', 'W', () => rnd(8, 180, 2)],
    ['Zähler Gesamt', '0/1/1', 'DPST-13-10', 'num', 'Wh', () => 29364000 + Math.floor(Math.random() * 800)],
    ['Leistung aktuell', '0/1/0', 'DPST-14-56', 'num', 'W', () => rnd(300, 1200, 2)],
    ['Luftdruck Pa', '0/2/9', 'DPST-14-58', 'num', 'Pa', () => rnd(101200, 101900, 2)],
    ['Absolute Feuchte g/m³', '0/2/8', 'DPST-14-17', 'num', 'g/m³', () => rnd(6, 12, 2)],
    ['Taupunkt', '0/2/6', 'DPST-9-1', 'num', '°C', () => rnd(4, 14, 2)],
    ['Feuchte', '0/2/5', 'DPST-9-7', 'num', '%', () => rnd(38, 62, 2)],
    ['Temperatur Nordseite', '0/2/4', 'DPST-9-1', 'num', '°C', () => rnd(16, 24, 2)],
    ['Garage rechts Temp.', '10/3/210', 'DPST-9-1', 'num', '°C', () => rnd(8, 22, 2)],
    ['DG Treppenhaus Helligkeit', '10/0/195', 'DPST-9-4', 'num', 'lx', () => rnd(40, 900, 0)],
    ['Garten Ventil 0 Haupt schalten', '20/1/236', 'DPST-1-11', 'bool', '', () => rb()],
    ['Garten Ventil 1 Terrasse schalten', '20/1/237', 'DPST-1-11', 'bool', '', () => rb()],
    ['Garten Ventil 2 Mitte schalten', '20/1/238', 'DPST-1-11', 'bool', '', () => rb()],
    ['Garten Ventil 3 Vorne schalten', '20/1/239', 'DPST-1-11', 'bool', '', () => rb()],
    ['EG Küche Fenster rechts FK', '10/2/46', 'DPST-1-1', 'bool', '', () => rb()],
    ['OG Schlafzimmer Fenster FK', '10/2/127', 'DPST-1-1', 'bool', '', () => rb()],
    ['OG Bad Licht schalten', '1/0/12', 'DPST-1-1', 'bool', '', () => rb()],
    ['EG Wohnen Licht Decke schalten', '1/0/3', 'DPST-1-1', 'bool', '', () => rb()],
    ['EG Wohnen Licht Decke dimmen', '1/1/3', 'DPST-5-1', 'num', '%', () => rnd(0, 100, 0)],
    ['West Status', '6/0/3', 'DPST-1-1', 'bool', '', () => rb()],
    ['Süd Status', '6/0/2', 'DPST-1-1', 'bool', '', () => rb()],
    ['Ost Status', '6/0/1', 'DPST-1-1', 'bool', '', () => rb()],
    ['Wind', '6/0/4', 'DPST-9-5', 'num', 'm/s', () => rnd(0, 14, 2)],
    ['OG Bad Heizung Soll', '2/3/7', 'DPST-9-1', 'num', '°C', () => rnd(18, 24, 1)],
    ['EG Jalousie Süd Höhe', '4/1/2', 'DPST-5-1', 'num', '%', () => rnd(0, 100, 0)],
    ['Luftdruck Interpretation Text', '0/2/10', 'DPST-16-0', 'text', '', () => pick(['heiter', 'wechselhaft', 'regnerisch', 'stabil'])],
  ];
  const SOURCES = ['1.1.9', '1.1.19', '1.1.63', '1.0.50', '1.0.61', '1.0.40', '1.0.60', '1.0.3', '1.0.15', '1.0.1'];
  const TYPES = ['write', 'write', 'write', 'write', 'read', 'response']; // weighted to write

  function rnd(a, b, d) { const v = a + Math.random() * (b - a); return d ? +v.toFixed(d) : Math.round(v); }
  function rb() { return Math.random() > 0.5; }
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }

  function hexFor(item, value) {
    if (item[3] === 'bool') return value ? '01' : '00';
    if (item[3] === 'text') return [...value].map(c => c.charCodeAt(0).toString(16).toUpperCase()).join('');
    // fake a plausible hex word for numbers
    const n = Math.abs(Math.round(value * 100)) % 0xFFFFFFFF;
    return n.toString(16).toUpperCase().padStart(8, '0').slice(0, 8);
  }
  function decodeStr(item, value) {
    if (item[3] === 'bool') return value ? 'On' : 'Off';
    if (item[3] === 'text') return '"' + value + '"';
    // German decimal formatting
    const dec = item[4] === 'Wh' || item[4] === 'lx' || item[4] === '%' && item[2] === 'DPST-5-1' ? 0 : 2;
    return value.toLocaleString('de-DE', { minimumFractionDigits: item[3] === 'num' && Number.isInteger(value) && item[4] === 'Wh' ? 0 : dec, maximumFractionDigits: dec });
  }

  let SEQ = 1;
  function makeTelegram(date) {
    const item = pick(CATALOG);
    const raw = item[5]();
    const value = raw;
    const valKind = item[3] === 'bool' ? (value ? 'on' : 'off') : item[3] === 'text' ? 'text' : 'num';
    return {
      id: SEQ++,
      ts: date.getTime(),
      time: fmtTime(date),
      datetime: fmtDateTime(date),
      src: pick(SOURCES),
      dst: item[1],
      name: item[0],
      dpt: item[2],
      type: pick(TYPES),
      raw: hexFor(item, value),
      value: decodeStr(item, value),
      valKind,
      unit: item[4],
      room: (item[0].match(/^(EG|OG|DG|UG|KG)\b/) || [null, null])[1],
    };
  }
  function fmtTime(d) {
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())},${String(d.getMilliseconds()).padStart(3, '0')}`;
  }
  function fmtDateTime(d) {
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${String(d.getFullYear()).slice(2)}, ${fmtTime(d)}`;
  }

  // build a history backlog (newest first)
  function buildHistory(count) {
    const out = [];
    let t = Date.now();
    for (let i = 0; i < count; i++) {
      t -= Math.floor(Math.random() * 1400) + 120;
      out.push(makeTelegram(new Date(t)));
    }
    return out;
  }

  window.KNXData = {
    makeTelegram: () => makeTelegram(new Date()),
    buildHistory,
    TYPES_ALL: ['write', 'read', 'response', 'groupread'],
    TYPE_LABEL: { write: 'Write', read: 'Read', response: 'Response', groupread: 'GroupRead' },
  };
})();
