import { chromium } from 'playwright-core';
import path from 'path';

const EXE = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'chromium-1228', 'chrome-win64', 'chrome.exe');
const FE = 'http://localhost:4200', API = 'http://localhost:8080/api';
const OUT = 'D:/Source/knx-ng-monitor/docs/screenshots/edit/qa';
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function login(page) {
  await page.goto(FE + '/login', { waitUntil: 'networkidle' });
  const t = await page.evaluate(async api =>
    (await fetch(api + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: 'demo', password: 'demo12345' }) })).json(), API);
  await page.evaluate(t => {
    localStorage.setItem('accessToken', t.accessToken);
    localStorage.setItem('refreshToken', t.refreshToken);
    localStorage.setItem('tokenExpiry', t.expiresAt);
    localStorage.setItem('username', t.username);
  }, t);
}

const b = await chromium.launch({ executablePath: EXE, headless: true });

// desktop
let ctx = await b.newContext({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 2 });
let page = await ctx.newPage();
await login(page);
await page.goto(FE + '/logs', { waitUntil: 'networkidle' });
await sleep(2500);
await page.screenshot({ path: OUT + '/logview_desktop.png' });
console.log('logview_desktop done');

// filtered to Warning
const warnBtn = page.getByRole('button', { name: /^Warn|Warning|Warnung/i }).first();
await warnBtn.click().catch(()=>{});
await sleep(800);
await page.screenshot({ path: OUT + '/logview_warning.png' });
console.log('logview_warning done');

// settings diagnostics card
await page.goto(FE + '/settings', { waitUntil: 'networkidle' });
await sleep(1500);
await page.screenshot({ path: OUT + '/logview_settings.png' });
console.log('settings done');
await ctx.close();

// mobile
ctx = await b.newContext({ viewport: { width: 412, height: 915 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
page = await ctx.newPage();
await login(page);
await page.goto(FE + '/logs', { waitUntil: 'networkidle' });
await sleep(2500);
await page.screenshot({ path: OUT + '/logview_mobile.png' });
console.log('logview_mobile done');
await ctx.close();

await b.close();
console.log('DONE');
