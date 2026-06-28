import { chromium } from 'playwright-core';
import path from 'path';
import fs from 'fs';

const EXE = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'chromium-1228', 'chrome-win64', 'chrome.exe');
const FE = 'http://localhost:4200', API = 'http://localhost:8080/api';
const OUT = 'D:/Source/knx-ng-monitor/docs/screenshots/edit/qa/gen';
fs.mkdirSync(OUT, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));
const FROM = '2026-06-24T08:30', TO = '2026-06-26T12:00';
const CHARTS = ['0/2/1', '0/2/4', '10/0/195', '10/0/90'];
const SENSOR = ['10/3/210', '10/0/90', '10/4/46', '20/1/236', '6/0/3', '10/1/65'];
const READ_GAS = '0/2/1,3/3/149,2/0/41,3/0/160,3/2/170,3/0/250,3/6/180,10/2/120,30/1/4,1/2/85,7/1/0,1/0/91,1/0/217,1/0/45,2/0/86,10/3/126,1/2/127,1/4/126,1/4/55,1/5/65,3/0/172,4/0/40,30/2/6,1/0/66,30/7/2,10/3/140,8/1/218,3/2/45,3/2/56,1/3/115,1/2/45,4/0/41,3/3/55,30/1/12,1/3/66,1/4/110,20/0/240,2/0/77,0/0/6,4/4/45'.split(',');

async function fireReads(page) {
  await page.evaluate(async (gas) => {
    const tok = localStorage.getItem('accessToken');
    await Promise.all(gas.map(a => fetch('http://localhost:8080/api/knx/read', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + tok },
      body: JSON.stringify({ address: a })
    }).catch(() => {})));
  }, READ_GAS);
}

async function token() {
  const r = await fetch(API + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: 'demo', password: 'demo12345' }) });
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
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.png` });

async function pickGas(page, addrs) {
  await page.locator('mat-select').first().click();
  await page.waitForSelector('mat-option', { timeout: 8000 });
  for (const a of addrs) {
    const o = page.locator('mat-option', { hasText: a + ' ' }).first();
    await o.scrollIntoViewIfNeeded(); await o.click(); await sleep(120);
  }
  await page.keyboard.press('Escape'); await sleep(400);
  const inputs = page.locator('input[type="datetime-local"]');
  await inputs.nth(0).fill(FROM); await inputs.nth(1).fill(TO);
  await page.getByRole('button', { name: /Anwenden|Apply/i }).first().click();
  await sleep(2800);
  await page.waitForSelector('.echart canvas', { timeout: 8000 }).catch(() => {});
  await sleep(1500);
}

async function run() {
  const b = await chromium.launch({ executablePath: EXE, headless: true });
  const t = await token();
  const log = [];
  const desk = { viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 2 };
  const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };

  async function screen(name, ctxOpts, fn) {
    const ctx = await b.newContext(ctxOpts);
    const page = await ctx.newPage();
    try { await fn(page); await shot(page, name); log.push(name + ' OK'); }
    catch (e) { log.push(name + ' FAIL: ' + e.message.split('\n')[0]); }
    await ctx.close();
  }

  // login (logged OUT)
  await screen('login', desk, async (p) => { await p.goto(FE + '/login', { waitUntil: 'networkidle' }); await sleep(1500); });

  // monitor live (bus connected -> trigger reads to populate the live feed)
  await screen('monitor-live', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
    await fireReads(p); await sleep(2500); await fireReads(p); await sleep(3000);
  });
  // hero-dark = monitor live, console theme
  await screen('hero-dark', desk, async (p) => {
    await auth(p, t, 'dark'); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500);
    await fireReads(p); await sleep(2500); await fireReads(p); await sleep(3000);
  });
  // monitor archive
  await screen('monitor-archive', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
    await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(3500);
  });
  // monitor detail (archive row click)
  await screen('monitor-detail', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(1500);
    await p.getByRole('button', { name: /Archive|Archiv/i }).first().click(); await sleep(3000);
    const row = p.locator('.knx-tr, .ag-row, tbody tr[role="row"], knx-table tr').first();
    await row.click({ timeout: 5000 }); await sleep(1500);
  });

  // charts
  await screen('charts', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200); await pickGas(p, CHARTS);
  });
  await screen('charts-temp', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200); await pickGas(p, SENSOR);
  });

  // stats + heatmap (same page)
  await screen('stats', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3000);
  });
  await screen('stats-heatmap', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3000);
    await p.locator('.heatmap-card').scrollIntoViewIfNeeded().catch(() => {}); await sleep(1000);
  });

  // topology, group-addresses, settings, graph
  await screen('topology', desk, async (p) => { await auth(p, t); await p.goto(FE + '/topology', { waitUntil: 'networkidle' }); await sleep(3000); });
  await screen('group-addresses', desk, async (p) => { await auth(p, t); await p.goto(FE + '/group-addresses', { waitUntil: 'networkidle' }); await sleep(3000); });
  await screen('settings', desk, async (p) => { await auth(p, t); await p.goto(FE + '/settings', { waitUntil: 'networkidle' }); await sleep(2000); });
  await screen('graph', desk, async (p) => { await auth(p, t); await p.goto(FE + '/graph', { waitUntil: 'networkidle' }); await sleep(6000); });
  await screen('logs', desk, async (p) => { await auth(p, t); await p.goto(FE + '/logs', { waitUntil: 'networkidle' }); await sleep(3000); });

  // projects + detail + import wizard
  await screen('projects', desk, async (p) => { await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2500); });
  await screen('projects-detail', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2500);
    await p.locator('tbody tr').first().locator('button.knx-btn--ghost').first().click({ timeout: 5000 }); await sleep(2000);
  });
  await screen('projects-import', desk, async (p) => {
    await auth(p, t); await p.goto(FE + '/projects', { waitUntil: 'networkidle' }); await sleep(2000);
    await p.getByRole('button', { name: /Import/i }).first().click({ timeout: 5000 }); await sleep(2000);
  });

  // mobiles
  await screen('monitor-live-mobile', phone, async (p) => { await auth(p, t); await p.goto(FE + '/monitor', { waitUntil: 'networkidle' }); await sleep(2500); await fireReads(p); await sleep(2500); await fireReads(p); await sleep(3000); });
  await screen('charts-mobile', phone, async (p) => { await auth(p, t); await p.goto(FE + '/charts', { waitUntil: 'networkidle' }); await sleep(1200); await pickGas(p, CHARTS); });
  await screen('stats-mobile', phone, async (p) => { await auth(p, t); await p.goto(FE + '/stats', { waitUntil: 'networkidle' }); await sleep(3000); });

  await b.close();
  console.log(log.join('\n'));
}
run().catch(e => { console.error('FATAL', e); process.exit(1); });
