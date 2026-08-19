/**
 * Voice-Over fuer den YouTube-Short (9:16). Gleiche Stimme wie das Langvideo,
 * aber acht kurze Saetze statt siebzehn ausfuehrlicher.
 *
 *   node tts_short.mjs        -> vo_short/s0..s7.mp3   (Deutsch)
 *   VLANG=en node tts_short.mjs -> vo_short_en/...     (Englisch)
 */
import fs from 'fs';
import { execSync } from 'child_process';

const KEY = fs.readFileSync(process.env.USERPROFILE + '/.claude/skills/video-use/.env', 'utf8').match(/ELEVENLABS_API_KEY=(.*)/)[1].trim();
const LANG = (process.env.VLANG || 'de').toLowerCase();
const OUT = 'D:/Source/knx-ng-monitor/docs/screenshots/edit/vo_short' + (LANG === 'en' ? '_en' : '');
fs.mkdirSync(OUT, { recursive: true });
const VOICE = 'dpsgxAAQscpwOkeRSVZr';

const SCRIPTS = {
  de: [
    ["s0", "KNX-NG Monitor macht deinen KNX-Bus sichtbar - in Echtzeit, direkt im Browser."],
    ["s1", "Jedes Telegramm läuft live durch und wird sofort dekodiert: echter Wert, echte Einheit."],
    ["s2", "Nichts geht verloren - alles wandert ins Archiv, durchsuchbar und filterbar."],
    ["s3", "Zahlenwerte werden zu Zeitreihen: Temperatur, Helligkeit, Leistung im Verlauf."],
    ["s4", "Die Statistik zeigt dir, wann auf dem Bus wirklich etwas los ist."],
    ["s5", "Dein ETS-Projekt importierst du direkt - Gruppenadressen und Geräte mit Klarnamen."],
    ["s6", "Läuft als Docker-Container oder portable Anwendung - am Desktop, am Handy, sogar auf dem Raspberry Pi."],
    ["s7", "Quelloffen, MIT-Lizenz, Beta-Tester gesucht. Schau auf GitHub vorbei."],
  ],
  en: [
    ["s0", "KNX-NG Monitor makes your KNX bus visible - in real time, right in the browser."],
    ["s1", "Every telegram streams in live and is decoded instantly: real value, real unit."],
    ["s2", "Nothing gets lost - everything lands in the archive, searchable and filterable."],
    ["s3", "Numbers become time series: temperature, brightness and power over time."],
    ["s4", "The statistics show you when your bus is actually busy."],
    ["s5", "Import your ETS project directly - group addresses and devices with real names."],
    ["s6", "Runs as a Docker container or portable app - on your desktop, your phone, even a Raspberry Pi."],
    ["s7", "Open source, MIT licensed, beta testers wanted. Come find it on GitHub."],
  ],
};

for (const [id, text] of SCRIPTS[LANG]) {
  const r = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${VOICE}?output_format=mp3_44100_128`, {
    method: 'POST', headers: { 'xi-api-key': KEY, 'Content-Type': 'application/json' },
    body: JSON.stringify({ text, model_id: 'eleven_v3', voice_settings: { stability: 0.5, similarity_boost: 0.75, use_speaker_boost: true } })
  });
  if (!r.ok) { console.log(id, 'ERR', r.status, (await r.text()).slice(0, 120)); continue; }
  fs.writeFileSync(`${OUT}/${id}.mp3`, Buffer.from(await r.arrayBuffer()));
  const dur = execSync(`ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "${OUT}/${id}.mp3"`).toString().trim();
  console.log(id, dur);
}
