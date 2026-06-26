import { chromium } from 'playwright-core';
import { fileURLToPath } from 'url';
import path from 'path';

const EXE = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'chromium-1228', 'chrome-win64', 'chrome.exe');
const BASE = 'http://localhost:4200';
const API = 'http://localhost:8080/api';
const OUT = 'D:/Source/knx-ng-monitor/docs/screenshots';
const FROM = '2026-06-24T08:30';
const TO   = '2026-06-25T16:00';

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

async function login(page) {
  await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
  const resp = await page.evaluate(async (api) => {
    const r = await fetch(api + '/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: 'demo', password: 'demo12345' })
    });
    return r.json();
  }, API);
  await page.evaluate((t) => {
    localStorage.setItem('accessToken', t.accessToken);
    localStorage.setItem('refreshToken', t.refreshToken);
    localStorage.setItem('tokenExpiry', t.expiresAt);
    localStorage.setItem('username', t.username);
  }, resp);
  console.log('login ok user=', resp.username);
}

async function pickGas(page, addresses) {
  // open the mat-select
  await page.locator('mat-select').first().click();
  await page.waitForSelector('mat-option', { timeout: 8000 });
  for (const addr of addresses) {
    const opt = page.locator('mat-option', { hasText: addr + ' ' }).first();
    await opt.scrollIntoViewIfNeeded();
    await opt.click();
    await sleep(120);
  }
  await page.keyboard.press('Escape');
  await sleep(400);
}

async function setRangeAndLoad(page) {
  const inputs = page.locator('input[type="datetime-local"]');
  await inputs.nth(0).fill(FROM);
  await inputs.nth(1).fill(TO);
  // Apply button
  await page.getByRole('button', { name: /Anwenden|Apply/i }).first().click();
  await sleep(2500);
}

async function shotCharts(page, name, addresses) {
  await page.goto(BASE + '/charts', { waitUntil: 'networkidle' });
  await sleep(1200);
  await pickGas(page, addresses);
  await setRangeAndLoad(page);
  // wait for canvas
  await page.waitForSelector('.echart canvas', { timeout: 8000 }).catch(() => {});
  await sleep(1500);
  await page.screenshot({ path: path.join(OUT, name) });
  console.log('shot', name);
}

(async () => {
  const browser = await chromium.launch({ executablePath: EXE, headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 2 });
  const page = await ctx.newPage();
  await login(page);

  // Beat 5 "Charts": multi-unit smooth (°C x2 / lux x2) — clean curves, per-unit Y axis
  await shotCharts(page, 'charts_new.png', ['0/2/1', '0/2/4', '10/0/195', '10/0/90']);

  // Beat 6 "Sensorkurven": numeric + boolean step lines
  await shotCharts(page, 'charts-temp_new.png', ['10/3/210', '10/0/90', '10/4/46', '20/1/236', '6/0/3', '10/1/65']);

  await browser.close();
  console.log('DONE');
})().catch(e => { console.error('FAIL', e); process.exit(1); });
