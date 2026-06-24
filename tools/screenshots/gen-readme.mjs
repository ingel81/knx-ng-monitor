// Builds the "## Screenshots" section of the README from the captured assets +
// manifest.json, then injects it between the SCREENSHOTS:START/END markers
// (inserted right after the badge block on first run). Idempotent — re-runnable.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join } from 'node:path';

const __dir = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dir, '../..');
const OUT = resolve(ROOT, 'docs/screenshots');
const README = join(ROOT, 'README.md');
const REL = 'docs/screenshots';

const START = '<!-- SCREENSHOTS:START -->';
const END = '<!-- SCREENSHOTS:END -->';

const manifest = JSON.parse(readFileSync(join(OUT, 'manifest.json'), 'utf8'));
const has = (f) => existsSync(join(OUT, f));

function cell(name, caption) {
  const full = `${REL}/${name}.webp`;
  const thumb = `${REL}/thumbs/${name}.webp`;
  const alt = caption.title;
  return `    <td align="center" width="50%">
      <a href="${full}"><img src="${thumb}" alt="${alt}" width="420"></a><br>
      <sub><b>${caption.title}</b><br>${caption.desc}</sub>
    </td>`;
}

function grid(shots, cols = 2) {
  const rows = [];
  for (let i = 0; i < shots.length; i += cols) {
    const chunk = shots.slice(i, i + cols);
    rows.push('  <tr>\n' + chunk.map((s) => cell(s.name, s.caption)).join('\n') + '\n  </tr>');
  }
  return '<table>\n' + rows.join('\n') + '\n</table>';
}

function mobileCell(name, caption) {
  if (!has(`${name}-mobile.webp`)) return '';
  return `    <td align="center">
      <a href="${REL}/${name}-mobile.webp"><img src="${REL}/thumbs/${name}-mobile.webp" alt="${caption.title} mobile" height="420"></a><br>
      <sub><b>${caption.title}</b></sub>
    </td>`;
}

function mobileGrid(shots, cols = 2) {
  const cells = shots.map((s) => mobileCell(s.name, s.caption)).filter(Boolean);
  const rows = [];
  for (let i = 0; i < cells.length; i += cols) {
    rows.push('  <tr>\n' + cells.slice(i, i + cols).join('\n') + '\n  </tr>');
  }
  return '<table>\n' + rows.join('\n') + '\n</table>';
}

// --- assemble ----------------------------------------------------------------
const present = manifest.shots.filter((s) => has(`${s.name}.webp`));
const lines = ['## Screenshots', ''];

// hero — animated GIF (renders on GitHub everywhere), linked to the crisp WebM.
if (has('hero.gif')) {
  const target = has('hero.webm') ? `${REL}/hero.webm` : `${REL}/hero.gif`;
  lines.push('<div align="center">', '',
    `<a href="${target}"><img src="${REL}/hero.gif" alt="KNX-NG-Monitor — live bus monitoring" width="900"></a>`,
    '', '</div>', '');
} else if (has('hero-dark.webp')) {
  lines.push(`<div align="center"><img src="${REL}/hero-dark.webp" alt="Monitor (dark theme)" width="900"></div>`, '');
}

lines.push('<sub>Click any thumbnail for the full-resolution image.</sub>', '');
lines.push('### Desktop', '', grid(present), '');

const mobileShots = present.filter((s) => has(`${s.name}-mobile.webp`));
if (mobileShots.length) {
  lines.push('### Mobile', '', mobileGrid(mobileShots, 3), '');
}

const section = `${START}\n${lines.join('\n')}\n${END}`;

let readme = readFileSync(README, 'utf8');
if (readme.includes(START) && readme.includes(END)) {
  readme = readme.replace(new RegExp(`${START}[\\s\\S]*?${END}`), section);
} else {
  // insert after the badge block: first blank line that follows a badge line
  const insertAfter = readme.indexOf('## Features');
  readme = readme.slice(0, insertAfter) + section + '\n\n' + readme.slice(insertAfter);
}
writeFileSync(README, readme);
console.log(`README updated: ${present.length} desktop shots, ${mobileShots.length} mobile/tablet, hero=${has('hero.webm') || has('hero.gif')}`);
