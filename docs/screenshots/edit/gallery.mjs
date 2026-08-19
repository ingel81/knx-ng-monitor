import { chromium } from 'playwright-core';
import path from 'path';
import fs from 'fs';

const EXE = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'chromium-1228', 'chrome-win64', 'chrome.exe');
// Port per Umgebungsvariable, weil der Dev-Server je nach belegtem 4200 woanders liegt:
//   FE_PORT=4321 node gallery.mjs
const FE = `http://localhost:${process.env.FE_PORT || 4200}`, API = `http://localhost:${process.env.API_PORT || 8080}/api`;
const OUT = process.env.SHOT_OUT || 'D:/Source/knx-ng-monitor/docs/screenshots/edit/qa/gen';
fs.mkdirSync(OUT, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));

// Datenbestand der Prod-Kopie: 2026-08-08 bis 2026-08-17 (1 Mio. Telegramme).
const FROM = '2026-08-14T00:00', TO = '2026-08-16T22:00';  // knapp 3 Tage - Tag/Nacht-Zyklen sichtbar
const CHARTS = ['0/2/1', '0/2/4', '10/0/195', '10/0/90'];
const SENSOR = ['10/3/210', '10/0/90', '10/4/46', '20/1/236', '6/0/3', '10/1/65'];
const READ_GAS = '0/2/1,3/3/149,2/0/41,3/0/160,3/2/170,3/0/250,3/6/180,10/2/120,30/1/4,1/2/85,7/1/0,1/0/91,1/0/217,1/0/45,2/0/86,10/3/126,1/2/127,1/4/126,1/4/55,1/5/65,3/0/172,4/0/40,30/2/6,1/0/66,30/7/2,10/3/140,8/1/218,3/2/45,3/2/56,1/3/115,1/2/45,4/0/41,3/3/55,30/1/12,1/3/66,1/4/110,20/0/240,2/0/77,0/0/6,4/4/45'.split(',');

// Personenbezug aus dem ETS-Projekt (Projektname, Gebäude, Strasse). Wird nicht
// ersetzt, sondern unkenntlich gemacht — so bleibt sichtbar, dass dort ein echter
// Wert steht, ohne ihn preiszugeben. Muster hier erweitern, wenn ein anderes
// Projekt importiert ist.
const SECRET_RX = 'ingelfinger|kilian';

/**
 * Legt einen Weichzeichner auf jedes Element, dessen Text das Muster trifft.
 * Direkt vor dem screenshot() aufrufen: Angular rendert sonst neu und der
 * Inline-Stil ist wieder weg.
 */
const blurSecrets = (page) => page.evaluate((rx) => {
  const re = new RegExp(rx, 'i');
  const targets = new Set();
  const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  let n;
  while (n = w.nextNode()) {
    if (n.nodeValue && re.test(n.nodeValue) && n.parentElement) targets.add(n.parentElement);
  }
  for (const el of document.querySelectorAll('[title],[aria-label]')) {
    for (const a of ['title', 'aria-label']) {
      const v = el.getAttribute(a);
      if (v && re.test(v)) { el.setAttribute(a, '***'); targets.add(el); }
    }
  }
  for (const el of targets) {
    el.style.filter = 'blur(10px)';
    el.style.userSelect = 'none';
  }
  return targets.size;
}, SECRET_RX);

async function token() {
  const r = await fetch(API + '/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'demo', password: 'demo12345' })
  });
  return r.json();
}

async function auth(page, t, theme) {
  await page.goto(FE + '/login', { waitUntil: 'networkidle' });
  await page.evaluate(({ t, theme }) => {
    localStorage.setItem('accessToken', t.accessToken);
    localStorage.setItem('refreshToken', t.refreshToken);
    localStorage.setItem('tokenExpiry', t.expiresAt);
    localStorage.setItem('username', t.username);
    if (theme) localStorage.setItem('knx.theme', theme);
  }, { t, theme });
}

async function fireReads(page) {
  await page.evaluate(async (gas) => {
    const tok = localStorage.getItem('accessToken');
    await Promise.all(gas.map(a => fetch('http://localhost:8080/api/knx/read', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + tok },
      body: JSON.stringify({ address: a })
    }).catch(() => { })));
  }, READ_GAS);
}

/**
 * Wartet, bis die Liste den sichtbaren Bereich wirklich überfüllt. Zeilen zählen
 * greift zu kurz: die Tabelle ist virtualisiert und rendert nur das Sichtbare —
 * die Anzahl bleibt also konstant, egal wie viele Telegramme eintreffen.
 */
async function waitFilled(page, sel, budgetMs = 120000) {
  const t0 = Date.now();
  let state = { count: 0, filled: false };
  while (Date.now() - t0 < budgetMs) {
    state = await page.evaluate((s) => {
      const vp = document.querySelector('.knx-vp') || document.querySelector(s)?.parentElement;
      const count = document.querySelectorAll(s).length;
      if (!vp) return { count, filled: count > 0 };
      // Der Spacer des Virtual Scroll trägt die Gesamthöhe.
      return { count, filled: vp.scrollHeight > vp.clientHeight * 1.15 };
    }, sel);
    if (state.filled) return state;
    // Der Bus liefert von selbst; Lesebefehle beschleunigen das Füllen.
    await fireReads(page).catch(() => { });
    await sleep(2500);
  }
  return state;
}

async function pickGas(page, addrs, withRange = true) {
  await page.locator('mat-select').first().click();
  await page.waitForSelector('mat-option', { timeout: 8000 });
  for (const a of addrs) {
    const o = page.locator('mat-option', { hasText: a + ' ' }).first();
    await o.scrollIntoViewIfNeeded(); await o.click(); await sleep(120);
  }
  await page.keyboard.press('Escape'); await sleep(400);
  if (withRange) {
    const inputs = page.locator('input[type="datetime-local"]');
    await inputs.nth(0).fill(FROM); await inputs.nth(1).fill(TO);
    await page.getByRole('button', { name: /Anwenden|Apply/i }).first().click();
  }
  await sleep(3000);
  await page.waitForSelector('.echart canvas', { timeout: 12000 }).catch(() => { });
  await sleep(2000);
}

const run = async () => {
  const b = await chromium.launch({ executablePath: EXE, headless: true, args: ['--lang=en-GB'] });
  const t = await token();
  const log = [];
  const desk = { viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 2, locale: 'en-GB' };
  const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'en-GB' };

  // ONLY=graph,topology schießt gezielt einzelne Bilder nach, ohne den ganzen Lauf.
  const only = (process.env.ONLY || '').split(',').map(s => s.trim()).filter(Boolean);

  async function screen(name, ctxOpts, fn, redactApi = false) {
    if (only.length && !only.includes(name)) return;
    const ctx = await b.newContext(ctxOpts);
    if (redactApi) {
      // Nur für Canvas-Ansichten: dort greift blurSecrets() nicht, weil der Text
      // gezeichnet und nicht als DOM-Knoten gerendert wird.
      // Das ganze Wort samt Hausnummer ersetzen, nicht nur den Treffer: sonst bleibt
      // aus "Kilianstraße 7" ein lesbares "•••••straße 7" stehen.
      const rx = new RegExp(`[\\wäöüÄÖÜß.\\-]*(?:${SECRET_RX})[\\wäöüÄÖÜß.\\-]*(?:\\s+\\d+[a-z]?)?`, 'gi');
      await ctx.route('**/api/**', async (route) => {
        const res = await route.fetch();
        const ct = res.headers()['content-type'] || '';
        if (!ct.includes('json')) return route.fulfill({ response: res });
        const body = await res.text();
        route.fulfill({ response: res, body: body.replace(rx, '•••••') });
      });
    }
    const page = await ctx.newPage();
    try {
      await fn(page);
      const blurred = await blurSecrets(page);
      await sleep(300);
      await page.screenshot({ path: `${OUT}/${name}.png` });
      log.push(`${name} OK${blurred ? `  (${blurred} unkenntlich)` : ''}`);
    } catch (e) {
      log.push(`${name} FAIL: ${e.message.split('\n')[0]}`);
    }
    await ctx.close();
  }

  await screen('login', desk, async p => { await p.goto(FE + '/login', { waitUntil: 'networkidle' }); await sleep(1500); });

  // Live-Ansichten: warten, bis die Liste den Bildschirm wirklich füllt.
  await screen('monitor-live', desk, async p => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
    const r = await waitFilled(p, '.knx-tr'); log.push(`   monitor-live rows=${r.count} gefuellt=${r.filled}`);
    await sleep(800);
  });
  await screen('hero-dark', desk, async p => {
    await auth(p, t, 'dark'); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
    const r = await waitFilled(p, '.knx-tr'); log.push(`   hero-dark rows=${r.count} gefuellt=${r.filled}`);
    await sleep(800);
  });
  await screen('monitor-archive', desk, async p => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
    await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(4500);
  });
  await screen('monitor-detail', desk, async p => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
    await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(4000);
    await p.locator('.knx-tr, .ag-row, tbody tr[role="row"]').first().click({ timeout: 6000 }); await sleep(1800);
  });

  await screen('charts', desk, async p => { await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200); await pickGas(p, CHARTS); });
  await screen('charts-temp', desk, async p => { await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200); await pickGas(p, SENSOR); });

  const pick7d = async (p) => { await p.getByRole('button', { name: /^7d$/ }).first().click().catch(() => {}); await sleep(4000); };
  await screen('stats', desk, async p => { await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3500); await pick7d(p); });
  await screen('stats-heatmap', desk, async p => {
    await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3500); await pick7d(p);
    await p.locator('.heatmap-card').scrollIntoViewIfNeeded().catch(() => { }); await sleep(1500);
  });

  await screen('topology', desk, async p => { await auth(p, t); await p.goto(FE + '/topology', { waitUntil: 'networkidle' }); await sleep(3500); });
  await screen('group-addresses', desk, async p => { await auth(p, t); await p.goto(FE + '/group-addresses', { waitUntil: 'networkidle' }); await sleep(3500); });
  await screen('settings', desk, async p => { await auth(p, t); await p.goto(FE + '/settings', { waitUntil: 'networkidle' }); await sleep(2500); });
  await screen('graph', desk, async p => { await auth(p, t); await p.goto(FE + '/graph', { waitUntil: 'networkidle' }); await sleep(9000); }, true);
  await screen('logs', desk, async p => { await auth(p, t); await p.goto(FE + '/logs', { waitUntil: 'networkidle' }); await sleep(3500); });

  await screen('projects', desk, async p => { await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2500); });
  await screen('projects-detail', desk, async p => {
    await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2500);
    await p.locator('tbody tr').first().locator('button.knx-btn--ghost').first().click({ timeout: 6000 }); await sleep(2200);
  });
  await screen('projects-import', desk, async p => {
    await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2000);
    await p.getByRole('button', { name: /Import/i }).first().click({ timeout: 6000 }); await sleep(2000);
  });

  await screen('monitor-live-mobile', phone, async p => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
    const r = await waitFilled(p, '.knx-mcard'); log.push(`   monitor-live-mobile cards=${r.count} gefuellt=${r.filled}`);
    await sleep(800);
  });
  await screen('charts-mobile', phone, async p => {
    await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200);
    await pickGas(p, CHARTS, false);
  });
  await screen('stats-mobile', phone, async p => { await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3500); await pick7d(p); });

  // Zusatz-Portraits nur fuer den YouTube-Short (9:16): dort fuellt eine Phone-Ansicht
  // das Bild randlos, waehrend ein Desktop-Screenshot als schmaler Streifen enden wuerde.
  // Nicht Teil der README-Galerie.
  if (process.env.SHORT_SHOTS !== '0') {
    await screen('hero-dark-mobile', phone, async p => {
      await auth(p, t, 'dark'); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
      const r = await waitFilled(p, '.knx-mcard'); log.push(`   hero-dark-mobile cards=${r.count} gefuellt=${r.filled}`);
      await sleep(800);
    });
    await screen('monitor-archive-mobile', phone, async p => {
      await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
      await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(4500);
    });
    await screen('monitor-detail-mobile', phone, async p => {
      await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
      await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(4000);
      await p.locator('.knx-mcard, .knx-tr').first().click({ timeout: 6000 }); await sleep(1800);
    });
    await screen('group-addresses-mobile', phone, async p => { await auth(p, t); await p.goto(FE + '/group-addresses', { waitUntil: 'networkidle' }); await sleep(3500); });
    await screen('topology-mobile', phone, async p => { await auth(p, t); await p.goto(FE + '/topology', { waitUntil: 'networkidle' }); await sleep(3500); });
    await screen('settings-mobile', phone, async p => { await auth(p, t); await p.goto(FE + '/settings', { waitUntil: 'networkidle' }); await sleep(2500); });
    await screen('projects-mobile', phone, async p => { await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2500); });
  }

  await b.close();
  console.log(log.join('\n'));
};
run().catch(e => { console.error('FATAL', e); process.exit(1); });
