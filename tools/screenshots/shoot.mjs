// Headless Playwright screenshot generator for the knx-ng-monitor README gallery.
//
// Drives the running dev apps (Angular :4200 / API :8080), captures every page in
// Full-HD @2x (light theme), a key subset for tablet + phone, plus a dark hero
// (still + animated WebM/GIF). PNG buffers are converted to WebP via sharp; thumbs
// are generated for the README grid. Privacy: the active project name is blurred.
//
// Prereqs: dev apps running + bus connected; screenshot user seeded; `npm install`
// + `npx playwright install chromium` done. Run: `node shoot.mjs`.

import { chromium } from 'playwright';
import sharp from 'sharp';
import { execFileSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join } from 'node:path';

const __dir = dirname(fileURLToPath(import.meta.url));
const OUT = resolve(__dir, '../../docs/screenshots');
const THUMBS = join(OUT, 'thumbs');
const TMP = join(__dir, '.frames');

const BASE = 'http://localhost:4200';
const API = 'http://127.0.0.1:8080/api';
const USER = 'demo';
const PASS = 'KnxShots2026!';
const LANG = 'en';
const CHARTS_GA = '0/1/0';      // "Leistung aktuell" (DPST-14-56) → lively live curve
const CHARTS_GA_TEMP = '0/2/1'; // "Temperatur Dach" (DPST-9-1)

// --- viewport profiles -------------------------------------------------------
const PROFILES = {
  desktop: { w: 1920, h: 1080, dsf: 2, suffix: '' },
  mobile:  { w: 390,  h: 844,  dsf: 2, suffix: '-mobile' },
};

// CSS injected before every shot: kill cursor, scrollbars, carets and DOM
// transitions/animations so captures are clean and deterministic.
const HYGIENE_CSS = `
  *,*::before,*::after { cursor: none !important;
    transition: none !important; animation: none !important; }
  *::-webkit-scrollbar { width: 0 !important; height: 0 !important; display: none !important; }
  * { scrollbar-width: none !important; }
  input, textarea { caret-color: transparent !important; }
`;

// Blur the active project name wherever it surfaces (only on /projects):
// the name cell, the file-name cell (contains the name) and the detail title.
const PROJECT_BLUR_CSS =
  `.proj-name, .proj-file, .details-card mat-card-title { filter: blur(7px) !important; }`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function login() {
  const res = await fetch(`${API}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: USER, password: PASS }),
  });
  if (!res.ok) throw new Error(`login failed: ${res.status}`);
  return res.json(); // { accessToken, refreshToken, expiresAt, username }
}

function initScript(tokens, theme) {
  // Runs before any app code on every navigation → primes auth + theme + lang.
  return `(() => {
    try {
      const t = ${JSON.stringify(tokens)};
      if (t && t.accessToken) {
        localStorage.setItem('accessToken', t.accessToken);
        localStorage.setItem('refreshToken', t.refreshToken);
        localStorage.setItem('tokenExpiry', t.expiresAt);
        localStorage.setItem('username', t.username);
      }
      localStorage.setItem('knx.theme', ${JSON.stringify(theme)});
      localStorage.setItem('knx.lang', ${JSON.stringify(LANG)});
      localStorage.setItem('knx.density', 'compact');
    } catch {}
  })();`;
}

async function makeContext(browser, profile, { authed, theme }) {
  const ctx = await browser.newContext({
    viewport: { width: profile.w, height: profile.h },
    deviceScaleFactor: profile.dsf,
    ignoreHTTPSErrors: true,
  });
  await ctx.addInitScript(initScript(authed ? TOKENS : null, theme));
  return ctx;
}

async function waitForAny(page, selectors, timeout = 20000) {
  try {
    await page.waitForSelector(selectors.join(', '), { timeout });
    return true;
  } catch {
    console.warn(`  ! none of [${selectors.join(', ')}] appeared`);
    return false;
  }
}

// Wait until the live/archive table has rows (best effort).
async function waitForRows(page, min = 8, timeout = 45000) {
  try {
    await page.waitForFunction(
      (m) => document.querySelectorAll('.knx-tr, .knx-mcard').length >= m,
      min, { timeout, polling: 500 },
    );
  } catch {
    const n = await page.evaluate(() => document.querySelectorAll('.knx-tr, .knx-mcard').length);
    console.warn(`  ! only ${n} rows after ${timeout}ms (continuing)`);
  }
}

async function prepare(page, css) {
  await page.addStyleTag({ content: HYGIENE_CSS + (css || '') });
  await sleep(350); // paint settle (not a data wait)
}

async function save(page, name) {
  const png = await page.screenshot({ type: 'png' });
  const full = join(OUT, `${name}.webp`);
  const thumb = join(THUMBS, `${name}.webp`);
  await sharp(png).webp({ quality: 82 }).toFile(full);
  await sharp(png).resize({ width: 1040 }).webp({ quality: 80 }).toFile(thumb);
  console.log(`  ✓ ${name}.webp`);
}

// --- shot catalogue ----------------------------------------------------------
// key: also captured for tablet+mobile. prep(page,profile): optional interaction.
const SHOTS = [
  {
    name: 'login', route: '/login', authed: false, key: true,
    wait: ['.login-button'],
    prep: async (page) => { await page.fill('input[name="username"]', 'demo').catch(() => {}); },
    caption: { title: 'Login', desc: 'JWT auth, dark/light aware' },
  },
  {
    name: 'monitor-live', route: '/monitor', key: true, mobile: true,
    wait: ['.knx-status'], rows: true, rowsMin: 26,
    caption: { title: 'Monitor — Live', desc: 'Real-time KNX telegrams off the bus' },
  },
  {
    name: 'monitor-detail', route: '/monitor',
    wait: ['.knx-status'], rows: true, rowsMin: 22,
    prep: async (page, profile) => {
      const rowSel = profile === 'mobile' ? '.knx-mcard' : '.knx-tr';
      await page.click(`${rowSel}`, { timeout: 8000 }).catch(() => {});
      await waitForAny(page, ['.knx-sheet-panel-right', '.knx-sheet-panel-bottom'], 8000);
      await sleep(300);
    },
    caption: { title: 'Telegram detail', desc: 'Value, bus actions, used-by, chart jump' },
  },
  {
    name: 'monitor-archive', route: '/monitor',
    wait: ['.knx-segmented'],
    prep: async (page) => {
      await page.click('.knx-segmented button:nth-child(2)').catch(() => {});
      await waitForAny(page, ['.filterbar'], 8000);
      await waitForRows(page, 5, 20000);
      await sleep(300);
    },
    caption: { title: 'Monitor — Archive', desc: 'Historized traffic, structured filters' },
  },
  {
    name: 'charts', route: `/charts?ga=${encodeURIComponent(CHARTS_GA)}`, key: true, mobile: true,
    wait: ['.echart canvas', '.empty-state'],
    prep: async () => sleep(1200), // let echarts render the series
    caption: { title: 'Charts — power', desc: 'Live value trends per group address' },
  },
  {
    name: 'charts-temp', route: `/charts?ga=${encodeURIComponent(CHARTS_GA_TEMP)}`,
    wait: ['.echart canvas', '.empty-state'],
    prep: async () => sleep(1200),
    caption: { title: 'Charts — temperature', desc: 'Sensor curve over time' },
  },
  {
    name: 'stats', route: '/stats', key: true, mobile: true,
    wait: ['.echart canvas', '.empty-state'],
    prep: async () => sleep(1000),
    caption: { title: 'Statistics', desc: 'Totals, msg/s, telegrams over time' },
  },
  {
    name: 'stats-heatmap', route: '/stats',
    wait: ['.echart canvas'],
    prep: async (page) => {
      await sleep(1200);
      await page.click('.knx-seg button:last-child').catch(() => {}); // 30d → full weeks
      await sleep(1900);
      await page.evaluate(() => document.querySelector('.heatmap-card')?.scrollIntoView({ block: 'center' }));
      await sleep(600);
    },
    caption: { title: 'Activity heatmap', desc: 'Telegrams by weekday × hour' },
  },
  {
    name: 'topology', route: '/topology', key: true, rewriteLocations: true,
    wait: ['.loc-tree', '.empty-state'],
    caption: { title: 'Topology', desc: 'Building tree with devices' },
  },
  {
    name: 'group-addresses', route: '/group-addresses', key: true,
    wait: ['.ga-tree', '.empty-state'],
    caption: { title: 'Group addresses', desc: '3-level tree, read / write / chart' },
  },
  {
    name: 'projects', route: '/projects', key: true,
    wait: ['.proj-table', '.placeholder'], blurProject: true,
    caption: { title: 'Projects', desc: 'ETS import, active project, auto-connect' },
  },
  {
    name: 'settings', route: '/settings', key: true,
    wait: ['.set-card'],
    caption: { title: 'Settings', desc: 'KNX interface, recording, theme & density' },
  },
  {
    name: 'graph', route: '/graph', key: true, rewriteLocations: true,
    wait: ['.graph-host canvas', '.empty-state'],
    prep: async () => sleep(5500),   // let the force layout settle + fit
    caption: { title: 'GA network — Alpha', desc: 'Force-directed building map, live activity' },
  },
  // --- optional dialogs (best effort; skipped silently on failure) ----------
  {
    name: 'projects-import', route: '/projects', optional: true, blurProject: true,
    wait: ['.proj-table', '.placeholder'],
    prep: async (page) => {
      await page.click('.knx-btn--primary', { timeout: 6000 });
      await waitForAny(page, ['mat-dialog-container'], 6000);
      await sleep(400);
    },
    caption: { title: 'Import wizard', desc: 'Two-stage ETS import with password / keyring' },
  },
  {
    name: 'projects-detail', route: '/projects', optional: true, blurProject: true,
    wait: ['.proj-table'],
    prep: async (page) => {
      await page.click('.proj-table .knx-btn--icon', { timeout: 6000 });
      await waitForAny(page, ['.details-overlay', '.details-card'], 6000);
      await sleep(400);
    },
    caption: { title: 'Project detail', desc: 'Group addresses & devices' },
  },
];

const KEY_NAMES = SHOTS.filter((s) => s.key).map((s) => s.name);

// CLI: --only=a,b  → shoot just those; --no-hero / --profiles=desktop,mobile
const ARGV = Object.fromEntries(process.argv.slice(2).map((a) => {
  const [k, v] = a.replace(/^--/, '').split('=');
  return [k, v ?? true];
}));
const ONLY = ARGV.only ? new Set(String(ARGV.only).split(',')) : null;
const PROFILE_FILTER = ARGV.profiles ? new Set(String(ARGV.profiles).split(',')) : null;

let TOKENS = null;

async function shootProfile(browser, profileName, shots) {
  const profile = PROFILES[profileName];
  console.log(`\n=== profile: ${profileName} (${profile.w}x${profile.h}@${profile.dsf}x) ===`);
  const authedCtx = await makeContext(browser, profile, { authed: true, theme: 'light' });
  const anonCtx = await makeContext(browser, profile, { authed: false, theme: 'light' });

  for (const shot of shots) {
    const fname = shot.name + profile.suffix;
    const ctx = shot.authed === false ? anonCtx : authedCtx;
    const page = await ctx.newPage();
    try {
      // Privacy: rewrite the locations API so Building/BuildingPart names (real
      // surname + street address) become neutral placeholders before they reach
      // the canvas graph / topology tree (canvas labels can't be CSS-blurred).
      if (shot.rewriteLocations) {
        await page.route('**/projects/*/locations', async (route) => {
          try {
            const resp = await route.fetch();
            const arr = await resp.json();
            const out = Array.isArray(arr) ? arr.map((l) =>
              l.type === 'Building' ? { ...l, name: 'Musterhaus' }
                : l.type === 'BuildingPart' ? { ...l, name: 'Musterstraße 1' }
                  : l) : arr;
            await route.fulfill({ response: resp, json: out });
          } catch { await route.continue(); }
        });
      }
      await page.goto(BASE + shot.route, { waitUntil: 'domcontentloaded' });
      await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
      await waitForAny(page, shot.wait);
      if (shot.rows) await waitForRows(page, shot.rowsMin ?? 8);
      if (shot.prep) await shot.prep(page, profileName);
      await prepare(page, shot.blurProject ? PROJECT_BLUR_CSS : '');
      await save(page, fname);
    } catch (err) {
      if (shot.optional) console.warn(`  ~ skipped ${fname}: ${err.message}`);
      else console.error(`  ✗ failed ${fname}: ${err.message}`);
    } finally {
      await page.close();
    }
  }
  await authedCtx.close();
  await anonCtx.close();
}

async function shootHero(browser) {
  console.log('\n=== hero (dark, desktop) ===');
  const profile = PROFILES.desktop;
  const ctx = await makeContext(browser, profile, { authed: true, theme: 'dark' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/monitor', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
  await waitForAny(page, ['.knx-status']);
  await waitForRows(page, 12, 45000);
  await prepare(page);

  // still
  const stillPng = await page.screenshot({ type: 'png' });
  await sharp(stillPng).webp({ quality: 84 }).toFile(join(OUT, 'hero-dark.webp'));
  console.log('  ✓ hero-dark.webp');

  // animated frames — live rows stream in as telegrams arrive
  rmSync(TMP, { recursive: true, force: true });
  mkdirSync(TMP, { recursive: true });
  const FRAMES = 44;   // ~20 s of live capture → ~17 s loop at 2.5 fps
  for (let i = 0; i < FRAMES; i++) {
    const buf = await page.screenshot({ type: 'png' });
    // downscale frames to keep encode fast / output small
    await sharp(buf).resize({ width: 1440 }).png().toFile(join(TMP, `f${String(i).padStart(3, '0')}.png`));
    await sleep(450);
  }
  await page.close();
  await ctx.close();

  // encode WebM (vp9) + GIF (palette) via ffmpeg
  try {
    execFileSync('ffmpeg', ['-y', '-framerate', '2.5', '-i', join(TMP, 'f%03d.png'),
      '-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '34', '-pix_fmt', 'yuv420p',
      join(OUT, 'hero.webm')], { stdio: 'ignore' });
    console.log('  ✓ hero.webm');
    execFileSync('ffmpeg', ['-y', '-framerate', '2.5', '-i', join(TMP, 'f%03d.png'),
      '-vf', 'scale=960:-2:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer',
      '-loop', '0', join(OUT, 'hero.gif')], { stdio: 'ignore' });
    console.log('  ✓ hero.gif');
  } catch (err) {
    console.error('  ✗ ffmpeg encode failed:', err.message);
  }
  rmSync(TMP, { recursive: true, force: true });
}

async function main() {
  mkdirSync(OUT, { recursive: true });
  mkdirSync(THUMBS, { recursive: true });
  TOKENS = await login();
  console.log(`logged in as ${TOKENS.username}`);

  const pick = (list) => (ONLY ? list.filter((s) => ONLY.has(s.name)) : list);
  const wantProfile = (p) => !PROFILE_FILTER || PROFILE_FILTER.has(p);

  const browser = await chromium.launch();
  try {
    if (ARGV['hero-only']) { await shootHero(browser); return; }
    if (wantProfile('desktop')) await shootProfile(browser, 'desktop', pick(SHOTS));
    // Mobile: only the cleanly-rendering key masks (tablet dropped — app header
    // overflows below the desktop layout at ~834px).
    if (wantProfile('mobile')) await shootProfile(browser, 'mobile', pick(SHOTS.filter((s) => s.mobile)));
    if (!ONLY && !ARGV['no-hero']) await shootHero(browser);
  } finally {
    await browser.close();
  }

  // emit caption manifest for the README generator
  const manifest = SHOTS.map((s) => ({
    name: s.name, caption: s.caption, key: !!s.key, optional: !!s.optional,
  }));
  writeFileSync(join(OUT, 'manifest.json'), JSON.stringify({ keyNames: KEY_NAMES, shots: manifest }, null, 2));
  console.log('\nmanifest.json written. Done.');
}

main().catch((e) => { console.error(e); process.exit(1); });
