/**
 * Nimmt die beiden bewegten Beats des Intro-Videos neu auf (s01 Desktop-Live, s12 Mobile).
 *
 * Warum CDP-Screencast und nicht Playwrights recordVideo: recordVideo liefert VP8 mit
 * fester Bitrate - Tabellentext franst sichtbar aus, waehrend alle anderen Beats aus
 * verlustfreien Screenshots kommen. Page.startScreencast gibt JPEG-Frames in
 * Viewport-Aufloesung heraus, die hier mit ihren Original-Zeitstempeln zu konstanten
 * 30 fps montiert werden.
 *
 * Aufruf:  FE_PORT=4321 API_PORT=8080 node rec_clips.mjs
 * Ergebnis: clips_live_desktop.mp4 (1920x1080) und clips_mobile_live.mp4 (Phone, roh).
 * Das Mobile-Composite (Teal-Hintergrund + Mint-Rahmen) baut danach ffmpeg, siehe REBUILD.md §3.
 */
import { chromium } from 'playwright-core';
import path from 'path';
import fs from 'fs';
import { execSync } from 'child_process';

const EXE = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'chromium-1228', 'chrome-win64', 'chrome.exe');
const FE = `http://localhost:${process.env.FE_PORT || 4200}`;
const API = `http://localhost:${process.env.API_PORT || 8080}/api`;
const HERE = 'D:/Source/knx-ng-monitor/docs/screenshots/edit';
const TMP = process.env.REC_TMP || path.join(HERE, 'qa', 'rec');
const sleep = ms => new Promise(r => setTimeout(r, ms));

const SECRET_RX = 'ingelfinger|kilian';
const READ_GAS = '0/2/1,3/3/149,2/0/41,3/0/160,3/2/170,3/0/250,3/6/180,10/2/120,30/1/4,1/2/85,7/1/0,1/0/91,1/0/217,1/0/45,2/0/86,10/3/126,1/2/127,1/4/126,1/4/55,1/5/65,3/0/172,4/0/40,30/2/6,1/0/66,30/7/2,10/3/140,8/1/218,3/2/45,3/2/56,1/3/115,1/2/45,4/0/41,3/3/55,30/1/12,1/3/66,1/4/110,20/0/240,2/0/77,0/0/6,4/4/45'.split(',');

/**
 * Anders als beim Standbild reicht ein einmaliger Weichzeichner hier nicht: die Liste
 * rendert waehrend der Aufnahme staendig neu. Der Intervall legt den Filter nach jedem
 * Angular-Render wieder auf.
 */
const keepBlurred = (page) => page.evaluate((rx) => {
  const re = new RegExp(rx, 'i');
  const apply = () => {
    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let n;
    while (n = w.nextNode()) {
      const el = n.parentElement;
      if (n.nodeValue && re.test(n.nodeValue) && el && el.style.filter !== 'blur(10px)') {
        el.style.filter = 'blur(10px)';
      }
    }
    for (const el of document.querySelectorAll('[title],[aria-label]')) {
      for (const a of ['title', 'aria-label']) {
        const v = el.getAttribute(a);
        if (v && re.test(v)) { el.setAttribute(a, '***'); el.style.filter = 'blur(10px)'; }
      }
    }
  };
  apply();
  window.__blurTimer = setInterval(apply, 400);
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

const fireReads = (page, api) => page.evaluate(async ({ gas, api }) => {
  const tok = localStorage.getItem('accessToken');
  await Promise.all(gas.map(a => fetch(api + '/knx/read', {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + tok },
    body: JSON.stringify({ address: a })
  }).catch(() => { })));
}, { gas: READ_GAS, api: API });

/** Wartet, bis die virtualisierte Liste den Viewport ueberfuellt (Zeilen zaehlen greift zu kurz). */
async function waitFilled(page, sel, budgetMs = 120000) {
  const t0 = Date.now();
  while (Date.now() - t0 < budgetMs) {
    const filled = await page.evaluate((s) => {
      const vp = document.querySelector('.knx-vp') || document.querySelector(s)?.parentElement;
      const count = document.querySelectorAll(s).length;
      if (!vp) return count > 0;
      return vp.scrollHeight > vp.clientHeight * 1.15;
    }, sel);
    if (filled) return true;
    await fireReads(page, API).catch(() => { });
    await sleep(2500);
  }
  return false;
}

/**
 * Zeichnet `seconds` Sekunden Screencast auf und montiert die Frames mit ihren
 * Original-Abstaenden zu einem konstanten 30-fps-Clip.
 */
async function record(page, name, seconds, maxW, maxH, keepBusy) {
  const dir = path.join(TMP, name);
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });

  const cdp = await page.context().newCDPSession(page);
  const frames = [];
  cdp.on('Page.screencastFrame', async (f) => {
    const i = frames.length;
    const file = path.join(dir, `f${String(i).padStart(5, '0')}.jpg`);
    fs.writeFileSync(file, Buffer.from(f.data, 'base64'));
    frames.push({ file, t: f.metadata.timestamp });
    try { await cdp.send('Page.screencastFrameAck', { sessionId: f.sessionId }); } catch { }
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 92, maxWidth: maxW, maxHeight: maxH, everyNthFrame: 1 });

  const t0 = Date.now();
  while (Date.now() - t0 < seconds * 1000) {
    if (keepBusy) await fireReads(page, API).catch(() => { });
    await sleep(1200);
  }
  await cdp.send('Page.stopScreencast');
  await sleep(400);

  if (frames.length < 5) throw new Error(`${name}: nur ${frames.length} Frames`);

  // Konstante Bildrate aus variabel eintreffenden Frames: Dauer = Abstand zum naechsten Frame.
  const list = [];
  for (let i = 0; i < frames.length; i++) {
    const d = i + 1 < frames.length ? Math.max(0.01, frames[i + 1].t - frames[i].t) : 1 / 30;
    list.push(`file '${path.basename(frames[i].file)}'`, `duration ${d.toFixed(4)}`);
  }
  list.push(`file '${path.basename(frames[frames.length - 1].file)}'`);
  fs.writeFileSync(path.join(dir, 'list.txt'), list.join('\n'));
  const span = frames[frames.length - 1].t - frames[0].t;
  console.log(`${name}: ${frames.length} Frames / ${span.toFixed(1)}s (${(frames.length / span).toFixed(1)} fps)`);
  return dir;
}

const run = async () => {
  fs.mkdirSync(TMP, { recursive: true });
  const only = process.env.REC_ONLY || 'both';
  const t = await token();

  if (only !== 'phone') await recordDesktop(t);
  if (only !== 'desktop') await recordPhone(t);
};

async function recordDesktop(t) {
  const b = await chromium.launch({ executablePath: EXE, headless: true, args: ['--lang=en-GB'] });
  // Desktop-Live 1:1 in Zielaufloesung - kein Rescaling, also keine Unschaerfe im Tabellentext.
  const deskCtx = await b.newContext({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, locale: 'en-GB' });
  const desk = await deskCtx.newPage();
  await auth(desk, t);
  await desk.goto(FE + '/monitor', { waitUntil: 'networkidle' });
  await sleep(2500);
  await waitFilled(desk, '.knx-tr');
  await keepBlurred(desk);
  await sleep(800);
  const deskDir = await record(desk, 'desktop', Number(process.env.REC_DESK || 16), 1920, 1080, true);
  await deskCtx.close();
  await b.close();
  enc(deskDir, path.join(HERE, 'clips_live_desktop.mp4'), 'fps=30,scale=1920:1080:flags=lanczos,setsar=1');
}

async function recordPhone(t) {
  // `--force-device-scale-factor=2` ist hier der Punkt: der Screencast liefert sonst
  // CSS-Pixel (390x844), also halbe Aufloesung, und das Phone-Bild wird im fertigen
  // Video sichtbar weich. Mit dem Flag rastert der Compositor in 780x1688.
  const b = await chromium.launch({ executablePath: EXE, headless: true, args: ['--lang=en-GB', '--force-device-scale-factor=2'] });
  const phoneCtx = await b.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'en-GB' });
  const phone = await phoneCtx.newPage();
  await auth(phone, t);
  await phone.goto(FE + '/monitor', { waitUntil: 'networkidle' });
  await sleep(2500);
  await waitFilled(phone, '.knx-mcard');
  await keepBlurred(phone);
  await sleep(800);
  const phoneDir = await record(phone, 'phone', Number(process.env.REC_PHONE || 12), 780, 1688, true);
  await phoneCtx.close();
  await b.close();
  enc(phoneDir, path.join(HERE, 'clips_mobile_live.mp4'), 'fps=30,setsar=1');
}

function enc(dir, out, vf) {
  const cmd = `ffmpeg -y -f concat -safe 0 -i "${path.join(dir, 'list.txt')}" -vf "${vf}" ` +
    `-r 30 -c:v libx264 -crf 16 -preset medium -pix_fmt yuv420p "${out}"`;
  execSync(cmd, { stdio: ['ignore', 'ignore', 'pipe'], cwd: dir });
  console.log('->', out);
}
run().catch(e => { console.error('FATAL', e); process.exit(1); });
