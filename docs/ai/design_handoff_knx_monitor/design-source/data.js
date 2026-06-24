/* KNX sample dataset: telegrams, devices, GA tree, topology, chart series, stats */
(function () {
  function rnd(a, b, d) { const v = a + Math.random() * (b - a); return d ? +v.toFixed(d) : Math.round(v); }
  function rb() { return Math.random() > 0.5; }
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }

  /* ---- devices (physical address → descriptor) used for SOURCE / USED BY ---- */
  const DEVICES = {
    '1.0.0':  'LK1 SCN-LK001.03 Linienkoppler/Verstärker',
    '1.0.1':  'SCN-IP100.02 IP Router',
    '1.0.3':  'BE-04230.02 Binäreingang 4-fach',
    '1.0.8':  'BE-04000.02 Binäreingang 4-fach',
    '1.0.10': 'BWM Bewegungsmelder Wohnen',
    '1.0.11': 'BWM Bewegungsmelder Bad',
    '1.0.12': 'BWM Bewegungsmelder Elternbad',
    '1.0.15': 'BE21 BE-02001.02 Tasterinterface',
    '1.0.40': 'GA Gartensteuerung Bewässerung',
    '1.0.50': 'TH-UP Temp./Feuchte Garage rechts',
    '1.0.51': 'TH-UP Temp./Feuchte Garage links',
    '1.0.58': 'P0 KNX-NG-Monitor (Tunnel)',
    '1.0.60': 'WS1 Wetterstation Premium',
    '1.0.61': 'WS1 Wetterstation Sensorik',
    '1.1.9':  'AZI-0616.01 Schaltaktor m. Wirkleistung',
    '1.1.17': 'AKH-0800.02 Heizungsaktor 8-fach',
    '1.1.18': 'AKH-0800.02 Heizungsaktor 8-fach',
    '1.1.19': 'P0 KNX Schnittstelle für Energiezähler',
  };
  function deviceFor(addr) { return DEVICES[addr] || 'Unbekanntes Gerät'; }

  // catalogue of group objects: [name, dst, dpt, kind, unit, decode(), room?]
  const CATALOG = [
    ['Leistung aktuell', '0/1/0', 'DPST-14-56', 'num', 'W', () => rnd(40, 1300, 1), null, '1.1.19'],
    ['Zähler Gesamt', '0/1/1', 'DPST-13-10', 'num', 'Wh', () => 29525000 + Math.floor(Math.random() * 900), null, '1.1.19'],
    ['Zähler Export Gesamt', '0/1/2', 'DPST-13-10', 'num', 'Wh', () => 918000 + Math.floor(Math.random() * 900), null, '1.1.19'],
    ['Frequenz', '0/1/5', 'DPST-14-33', 'num', 'Hz', () => rnd(49.9, 50.1, 2), null, '1.1.19'],
    ['Spannung', '0/1/6', 'DPST-14-27', 'num', 'V', () => rnd(228, 235, 1), null, '1.1.19'],
    ['Strom', '0/1/7', 'DPST-14-19', 'num', 'A', () => rnd(0.2, 6, 2), null, '1.1.19'],
    ['Luftdruck Pa', '0/2/9', 'DPST-14-58', 'num', 'Pa', () => rnd(101200, 101900, 0), null, '1.0.61'],
    ['Absolute Feuchte g/m³', '0/2/8', 'DPST-14-17', 'num', 'kg/m³', () => rnd(13, 16, 3), null, '1.0.61'],
    ['Absolute Feuchte g/kg', '0/2/7', 'DPST-14-5', 'num', '', () => rnd(11, 14, 3), null, '1.0.61'],
    ['Taupunkt', '0/2/6', 'DPST-9-1', 'num', '°C', () => rnd(15, 19, 1), null, '1.0.61'],
    ['Feuchte', '0/2/5', 'DPST-9-7', 'num', '%', () => rnd(45, 55, 2), null, '1.0.61'],
    ['Temperatur Nordseite', '0/2/4', 'DPST-9-1', 'num', '°C', () => rnd(24, 31, 1), null, '1.0.61'],
    ['Temperatur Dach', '0/2/1', 'DPST-9-1', 'num', '°C', () => rnd(24, 31, 1), null, '1.0.60'],
    ['Helligkeit', '0/2/3', 'DPST-9-4', 'num', 'Lux', () => rnd(60000, 66000, 0), null, '1.0.60'],
    ['Wind', '0/2/2', 'DPST-9-5', 'num', 'm/s', () => rnd(0, 3, 1), null, '1.0.60'],
    ['West Status', '6/0/3', 'DPST-1-1', 'bool', '', () => rb(), null, '1.0.60'],
    ['Süd Status', '6/0/2', 'DPST-1-1', 'bool', '', () => rb(), null, '1.0.60'],
    ['Ost Status', '6/0/1', 'DPST-1-1', 'bool', '', () => rb(), null, '1.0.60'],
    ['Uhrzeit GPS', '0/0/11', 'DPST-10-1', 'time', '', () => rnd(0, 1, 0), null, '1.0.60'],
    ['Datum GPS', '0/0/12', 'DPST-11-1', 'date', '', () => rnd(0, 1, 0), null, '1.0.60'],
    ['Garten Ventil 0 Haupt schalten status', '20/1/236', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Garten Ventil 1 Terasse schalten status', '20/1/237', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Garten Ventil 2 Mitte schalten status', '20/1/238', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Garten Ventil 3 Vorne schalten status', '20/1/239', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Garten Ventil 4 Beet 1 schalten status', '20/1/240', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Garten Ventil 5 Beet 2 schalten status', '20/1/241', 'DPST-1-11', 'bool', '', () => false, null, '1.0.40'],
    ['Küche Geschirrspüler Leistung', '10/4/46', 'DPST-14-56', 'num', 'W', () => rnd(0, 2200, 2), 'EG', '1.1.9'],
    ['Wohnen TV Steckd. 5 Leistung', '10/4/77', 'DPST-14-56', 'num', 'W', () => rnd(0, 60, 2), 'EG', '1.1.9'],
    ['Küche Fenster rechts FK', '10/2/46', 'DPST-1-1', 'bool', '', () => rb(), 'EG', '1.0.3'],
    ['Wohnen TV Couch hinten FK', '10/2/75', 'DPST-1-1', 'bool', '', () => rb(), 'EG', '1.0.8'],
    ['Büro FK', '10/2/85', 'DPST-1-1', 'bool', '', () => rb(), 'EG', '1.0.10'],
    ['Schlafzimmer Fenster FK', '10/2/127', 'DPST-1-1', 'bool', '', () => rb(), 'OG', '1.0.15'],
    ['Kinderbad FK', '10/2/115', 'DPST-1-1', 'bool', '', () => rb(), 'OG', '1.0.11'],
    ['Elternbad FK', '10/2/120', 'DPST-1-1', 'bool', '', () => rb(), 'OG', '1.0.12'],
    ['Kind1 Sollwert Heizung', '4/0/145', 'DPST-9-1', 'num', '°C', () => rnd(20, 22, 1), 'OG', '1.1.18'],
    ['Kind2 Sollwert Heizung', '4/0/150', 'DPST-9-1', 'num', '°C', () => rnd(20, 22, 1), 'OG', '1.1.18'],
    ['Küche Stellwert Status', '4/4/45', 'DPST-5-1', 'num', '%', () => 0, 'EG', '1.1.17'],
    ['HWR Stellwert Status', '4/4/40', 'DPST-5-1', 'num', '%', () => 0, 'EG', '1.1.17'],
    ['Wohnen Stellwert Status', '4/4/65', 'DPST-5-1', 'num', '%', () => 0, 'EG', '1.1.17'],
    ['Garage rechts Temp.', '10/3/210', 'DPST-9-1', 'num', '°C', () => rnd(18, 26, 1), null, '1.0.50'],
    ['Garage links Temp.', '10/3/211', 'DPST-9-1', 'num', '°C', () => rnd(18, 26, 1), null, '1.0.51'],
  ];

  const TYPES = ['write', 'write', 'write', 'write', 'write', 'read', 'response'];
  const TOPIC_FOR = name => {
    if (/temp|°c|taupunkt/i.test(name)) return 'temperature';
    if (/licht|dimm|hellig/i.test(name)) return 'light';
    if (/jalousie|status|wind|west|süd|ost|ventil/i.test(name)) return 'shading';
    if (/leistung|zähler|spannung|strom|frequenz|\bw\b/i.test(name)) return 'power';
    return null;
  };

  function hexFor(item, value) {
    if (item[3] === 'bool') return value ? '01' : '00';
    if (item[3] === 'time') return 'AA' + String(rnd(0,9,0)) + '601';
    if (item[3] === 'date') return '13061A';
    const n = Math.abs(Math.round(value * 1000)) % 0xFFFFFFFF;
    return n.toString(16).toUpperCase().padStart(8, '0').slice(0, 8);
  }
  function decodeStr(item, value) {
    if (item[3] === 'bool') {
      if (/ventil|status status/i.test(item[0]) && item[2] === 'DPST-1-11') return value ? 'Active' : 'Inactive';
      return value ? 'On' : 'Off';
    }
    if (item[3] === 'time') return 'Fri 10:06';
    if (item[3] === 'date') return '2026-06-19';
    const dec = (item[4] === 'Wh' || item[4] === 'Lux' || item[4] === 'Pa' || (item[4] === '%' && item[2] === 'DPST-5-1')) ? 0
      : (item[2] === 'DPST-14-5' || item[2] === 'DPST-14-17') ? 3 : (item[4] === 'W' || item[4] === '°C' || item[4] === 'm/s') ? 1 : 2;
    return value.toLocaleString('de-DE', { minimumFractionDigits: dec, maximumFractionDigits: dec });
  }

  let SEQ = 1;
  function makeTelegram(date) {
    const item = pick(CATALOG);
    const raw = item[5]();
    const valKind = item[3] === 'bool' ? (raw ? 'on' : 'off') : (item[3] === 'time' || item[3] === 'date' || item[3] === 'text') ? 'text' : 'num';
    return {
      id: SEQ++, ts: date.getTime(),
      time: fmtTime(date), datetime: fmtDateTime(date),
      src: item[7], srcName: deviceFor(item[7]),
      dst: item[1], name: item[0], dpt: item[2],
      type: pick(TYPES), raw: hexFor(item, raw),
      value: decodeStr(item, raw), valKind, unit: item[4],
      topic: TOPIC_FOR(item[0]),
      room: item[6],
      priority: 'Low', flags: '00',
    };
  }
  function fmtTime(d) {
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}.${String(d.getMilliseconds()).padStart(3, '0')}`;
  }
  function fmtDateTime(d) {
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${String(d.getFullYear()).slice(2)}, ${fmtTime(d)}`;
  }
  function buildHistory(count) {
    const out = []; let t = Date.now();
    for (let i = 0; i < count; i++) { t -= Math.floor(Math.random() * 1100) + 120; out.push(makeTelegram(new Date(t))); }
    return out;
  }

  /* ----------------------------- Group-address tree ----------------------------- */
  const GA_TREE = [
    { id: '0', name: 'Zentral', children: [
      { id: '0/0', name: 'Uhrzeit/Datum', gas: [
        ['0/0/1', 'Uhrzeit', 'DPST-10-1', 'val'], ['0/0/2', 'Datum', 'DPST-11-1', 'val'],
        ['0/0/3', 'Datum-Uhrzeit', 'DPST-19-1', 'val'], ['0/0/4', 'Nacht', 'DPST-1-1', 'bool'],
        ['0/0/5', 'Tag', 'DPST-1-1', 'bool'], ['0/0/6', 'Sommer=1/Winter=0', 'DPST-1-1', 'bool'],
        ['0/0/11', 'Uhrzeit GPS', 'DPST-10-1', 'val'], ['0/0/12', 'Datum GPS', 'DPST-11-1', 'val'],
        ['0/0/13', 'Störung GPS', 'DPST-1-1', 'bool'], ['0/0/14', 'Test Sommer(1) Reloaded', 'DPST-1-1', 'bool'],
      ]},
      { id: '0/1', name: 'Verbrauchsdaten', gas: [
        ['0/1/0', 'Leistung aktuell', 'DPST-14-56', 'val'], ['0/1/1', 'Zähler Gesamt', 'DPST-13-10', 'val'],
        ['0/1/2', 'Zähler Export Gesamt', 'DPST-13-10', 'val'], ['0/1/5', 'Frequenz', 'DPST-14-33', 'val'],
        ['0/1/6', 'Spannung', 'DPST-14-27', 'val'], ['0/1/7', 'Strom', 'DPST-14-19', 'val'],
      ]},
      { id: '0/2', name: 'Wetterstation', gas: [
        ['0/2/1', 'Temperatur Dach', 'DPST-9-1', 'val'], ['0/2/2', 'Wind', 'DPST-9-5', 'val'],
        ['0/2/3', 'Helligkeit', 'DPST-9-4', 'val'], ['0/2/4', 'Temperatur Nordseite', 'DPST-9-1', 'val'],
        ['0/2/5', 'Feuchte', 'DPST-9-7', 'val'], ['0/2/6', 'Taupunkt', 'DPST-9-1', 'val'],
      ]},
    ]},
    { id: '4', name: 'Heizung', children: [
      { id: '4/0', name: 'Sollwerte', gas: [
        ['4/0/145', 'Kind1 Sollwert Heizung', 'DPST-9-1', 'val'], ['4/0/150', 'Kind2 Sollwert Heizung', 'DPST-9-1', 'val'],
      ]},
      { id: '4/4', name: 'Stellwerte', gas: [
        ['4/4/40', 'HWR Stellwert Status', 'DPST-5-1', 'val'], ['4/4/45', 'Küche Stellwert Status', 'DPST-5-1', 'val'],
        ['4/4/65', 'Wohnen Stellwert Status', 'DPST-5-1', 'val'], ['4/4/85', 'Büro Stellwert Status', 'DPST-5-1', 'val'],
      ]},
    ]},
    { id: '10', name: 'Räume EG/OG', children: [
      { id: '10/2', name: 'Fensterkontakte', gas: [
        ['10/2/40', 'HWR Fenster FK', 'DPST-1-1', 'bool'], ['10/2/46', 'Küche Fenster rechts FK', 'DPST-1-1', 'bool'],
        ['10/2/75', 'Wohnen TV Couch hinten FK', 'DPST-1-1', 'bool'], ['10/2/115', 'Kinderbad FK', 'DPST-1-1', 'bool'],
        ['10/2/120', 'Elternbad FK', 'DPST-1-1', 'bool'], ['10/2/127', 'Schlafzimmer Fenster FK', 'DPST-1-1', 'bool'],
      ]},
      { id: '10/4', name: 'Leistungsmessung', gas: [
        ['10/4/45', 'Küche Kühlschrank Leistung', 'DPST-14-56', 'val'], ['10/4/46', 'Küche Geschirrspüler Leistung', 'DPST-14-56', 'val'],
        ['10/4/77', 'Wohnen TV Steckd. 5 Leistung', 'DPST-14-56', 'val'],
      ]},
    ]},
    { id: '20', name: 'Garten', children: [
      { id: '20/1', name: 'Bewässerung', gas: [
        ['20/1/236', 'Garten Ventil 0 Haupt schalten', 'DPST-1-11', 'bool'], ['20/1/237', 'Garten Ventil 1 Terasse schalten', 'DPST-1-11', 'bool'],
        ['20/1/238', 'Garten Ventil 2 Mitte schalten', 'DPST-1-11', 'bool'], ['20/1/239', 'Garten Ventil 3 Vorne schalten', 'DPST-1-11', 'bool'],
      ]},
    ]},
  ];
  const GA_COUNT = 843;

  /* ----------------------------- Topology tree ----------------------------- */
  const TOPOLOGY = {
    name: 'myHome', type: 'Building', children: [
      { name: 'Haupthaus', type: 'BuildingPart', children: [
        { name: 'Kellergeschoss', type: 'Floor', children: [
          { name: 'Flur KG', type: 'Room', devices: [
            'BWM1 Bewegungsmelder Standard 1,10 m (1.1.30)',
            'Wasserzähler1 - KW Wasserzähler KWZC FacilityWeb (1.1.64)',
            'Wasserzähler2 - WW Wasserzähler KWZC FacilityWeb (1.1.65)',
          ]},
          { name: 'Heizraum', type: 'Room', devices: [] },
          { name: 'Werkstatt', type: 'Room', devices: [
            'BE21 BE-02001.02 Tasterinterface 2fach, UP (1.1.32)',
            'BWM5 Bewegungsmelder Standard 1,10 m (1.1.61)',
          ]},
          { name: 'Technik', type: 'Room', children: [
            { name: 'UV Haus', type: 'DistributionBoard', devices: [
              'LK1 SCN-LK001.03 Linienkoppler/Verstärker mit Data Secure (1.1.0)',
              'SA1 AKK-1616.03 Schaltaktor 16fach, 8TE, 16A (1.1.6)',
              'SA2 AKK-1616.03 Schaltaktor 16fach, 8TE, 16A (1.1.7)',
              'SA3 AKK-1616.03 Schaltaktor 16fach, 8TE, 16A (1.1.8)',
              'DA1 AKD-0401.02 Dimmaktor 4-fach (1.1.10)',
              'DA2 AKD-0401.02 Dimmaktor 4-fach (1.1.11)',
              'DA3 AKD-0401.02 Dimmaktor 4-fach (1.1.12)',
              'DA4 AKD-0401.02 Dimmaktor 4-fach (1.1.13)',
              'SA4 AZI-0616.01 Schaltaktor 6-fach mit Wirkleistungszähler (1.1.9)',
              'JA1 JAL-0810.02 Jalousieaktor 8-fach, 8TE, 230VAC, 10A (1.1.14)',
              'JA2 JAL-0810.02 Jalousieaktor 8-fach, 8TE, 230VAC, 10A (1.1.15)',
              'JA3 JAL-0810.02 Jalousieaktor 8-fach, 8TE, 230VAC, 10A (1.1.16)',
              'HA1 AKH-0800.02 Heizungsaktor 8-fach, 4TE, 24/230 VAC (1.1.17)',
              'HA2 AKH-0800.02 Heizungsaktor 8-fach, 4TE, 24/230 VAC (1.1.18)',
              'P0 KNX Schnittstelle für Energiezähler (1.1.19)',
              'SCN-IP100.02/.03 IP Router without Secure (1.1.1)',
              'BE-04230.02 Binary Input 4-fold, 2SU, Inputs 230VAC (1.1.21)',
              'BE-04000.02 Binary Input 4-fold, 2SU, Contact inputs (1.1.22)',
            ]},
          ]},
        ]},
        { name: 'Erdgeschoss', type: 'Floor', children: [
          { name: 'Küche', type: 'Room', devices: ['BWM Bewegungsmelder Wohnen (1.0.10)', 'BE-04000.02 Binäreingang (1.0.3)'] },
          { name: 'Wohnen', type: 'Room', devices: ['BE-04000.02 Binäreingang 4-fach (1.0.8)'] },
          { name: 'Büro', type: 'Room', devices: ['BWM Bewegungsmelder Büro (1.0.10)'] },
        ]},
        { name: 'Obergeschoss', type: 'Floor', children: [
          { name: 'Schlafzimmer', type: 'Room', devices: ['BE21 Tasterinterface (1.0.15)'] },
          { name: 'Kind 1', type: 'Room', devices: [] },
          { name: 'Kind 2', type: 'Room', devices: [] },
          { name: 'Elternbad', type: 'Room', devices: ['BWM Bewegungsmelder (1.0.12)'] },
        ]},
      ]},
    ],
  };

  /* ----------------------------- Chart series ----------------------------- */
  // 24h profile, ~ one point / 3 min => 480 points, starting "yesterday 22:30"
  function seriesPower() {
    const n = 480, out = []; const start = Date.now() - 24 * 3600e3;
    for (let i = 0; i < n; i++) {
      const t = start + i * (24 * 3600e3 / n);
      const h = new Date(t).getHours() + new Date(t).getMinutes() / 60;
      let base = 900 - 380 * Math.cos((h - 4) / 24 * 2 * Math.PI);     // gentle daily swing
      if (h > 4.4 && h < 6.2) base += 350 + 1100 * Math.max(0, Math.sin((h - 4.4) / 1.8 * Math.PI)); // morning ramp
      let v = base + rnd(-90, 90, 0);
      if (h > 4.5 && h < 6 && Math.random() > 0.86) v += rnd(900, 2300, 0); // appliance spikes
      if (h > 6.9 && h < 7.4) v = rnd(-20, 40, 0);                            // PV / off dip
      if (h > 7.4) v = Math.max(0, 120 + 140 * Math.sin((h - 7.4) * 2) + rnd(-40, 80, 0));
      out.push([t, Math.max(-30, Math.round(v))]);
    }
    return out;
  }
  function seriesTemp() {
    const n = 480, out = []; const start = Date.now() - 24 * 3600e3;
    for (let i = 0; i < n; i++) {
      const t = start + i * (24 * 3600e3 / n);
      const h = new Date(t).getHours() + new Date(t).getMinutes() / 60;
      let v = 20 - 2.2 * Math.cos((h - 14) / 24 * 2 * Math.PI);
      if (h >= 4) v = 18 + (h - 4) * 1.15;            // morning sun on roof
      v += rnd(-0.35, 0.35, 2);
      out.push([t, +Math.max(17, v).toFixed(1)]);
    }
    return out;
  }
  const CHART_GAS = [
    { id: '0/1/0', name: 'Leistung aktuell', unit: 'W', series: seriesPower },
    { id: '0/2/1', name: 'Temperatur Dach', unit: '°C', series: seriesTemp },
    { id: '0/2/4', name: 'Temperatur Nordseite', unit: '°C', series: seriesTemp },
    { id: '0/1/1', name: 'Zähler Gesamt', unit: 'Wh', series: seriesPower },
    { id: '0/2/5', name: 'Feuchte', unit: '%', series: () => seriesTemp().map(([t, v]) => [t, +(v * 2 + 8).toFixed(1)]) },
  ];

  /* ----------------------------- Statistics ----------------------------- */
  function statsBars() {
    const n = 47, out = []; const start = Date.now() - 24 * 3600e3;
    for (let i = 0; i < n; i++) {
      const t = start + i * (24 * 3600e3 / n);
      const h = new Date(t).getHours();
      let v = h < 6 ? rnd(1300, 2100, 0) : rnd(1700, 2500, 0);
      if (i < 1) v = rnd(800, 1000, 0);
      out.push([t, v]);
    }
    return out;
  }

  window.KNXData = {
    makeTelegram: () => makeTelegram(new Date()),
    buildHistory, deviceFor,
    GA_TREE, GA_COUNT, TOPOLOGY, CHART_GAS, statsBars,
    TOTAL_TELEGRAMS: 55780, TOTAL_24H: 46233, AVG_MSGS: 0.54,
    PROJECT_GAS: 843, PROJECT_DEVICES: 94,
    TYPE_LABEL: { write: 'Write', read: 'Read', response: 'Response', groupread: 'GroupRead' },
  };
})();
