import fs from 'fs';
import { execSync } from 'child_process';
const KEY = fs.readFileSync(process.env.USERPROFILE + '/.claude/skills/video-use/.env','utf8').match(/ELEVENLABS_API_KEY=(.*)/)[1].trim();
const OUT='D:/Source/knx-ng-monitor/docs/screenshots/edit/vo_en';
fs.mkdirSync(OUT,{recursive:true});
const VOICE='JBFqnCBsd6RMkjVDRZzb'; // George - Warm, Captivating Storyteller (UK)
const lines = [
 ["s00","KNX-NG Monitor makes your KNX bus visible. Every telegram in real time — captured automatically, stored permanently, and clearly presented. Open source, and fully hosted on your own."],
 ["s01","In the live monitor, all bus traffic streams through the moment it happens. The connection stays up permanently and recovers on its own after a dropout. Every telegram is decoded instantly — you see the real value with its unit, instead of just raw bytes."],
 ["s02","And nothing gets lost: every telegram is archived without gaps. You search the entire history by full text, filter by address, type, or room — with no volume limit. When needed, you export everything as a file."],
 ["s03","For every telegram there's a full detail view, with value, timestamp, and origin. And you're not just a spectator: values can be written straight onto the bus from the tool, or read on demand."],
 ["s04","Numeric values are shown as time series. Each unit gets its own axis, so temperature and power, for example, stay readable together. New readings come in live while you watch."],
 ["s05","And it doesn't matter whether it's measured values or switching events. Temperature, brightness, power, or simple on-off states — everything lands in the same chart and becomes comparable over time."],
 ["s06","The statistics give you the overview: how many telegrams were on the move in total, how many per second on average, and how the traffic spreads over time."],
 ["s07","The activity heatmap shows when there's really something going on the bus. Weekday by hour — so you spot routines and recurring patterns at a glance."],
 ["s08","You bring your existing ETS project in comfortably through a wizard — whether ETS 4, 5, or 6. Group addresses, devices, and hardware are imported automatically, so everything shows up with clear names right away."],
 ["s09","KNX Secure is supported too. Password-protected projects, decryption through the keyring, and Data Secure at runtime — so encrypted telegrams are made readable directly."],
 ["s10","The topology maps your installation — from the building, through the floor, down to the individual room. Plus the communication objects and a room filter to focus in on a specific area."],
 ["s11","You'll find all group addresses in the searchable three-level tree. From there you read or write any value directly — or open it as a chart with a single click."],
 ["s12","And the whole thing is fully responsive. Whether on your phone on the go, or on a tablet on the couch — the interface adapts and stays just as usable as on the desktop."],
 ["s13","A light theme for daytime, a dark one for dimmed rooms. German or English, switchable live at any time — whatever you prefer."],
 ["s14","It runs practically everywhere: as a Docker container or a portable application, on Linux, Windows, and macOS — and thanks to ARM, even on a Raspberry Pi. Everything is operated in the browser, and stays completely local to you."],
 ["s15","Still experimental, but exciting: the network graph of your group addresses. From the building, through floor and room, down to the individual address — and incoming telegrams make the matching nodes briefly light up."],
 ["s16","KNX-NG Monitor is open source under the MIT license. The project is currently looking for beta testers — whether you're a KNX pro or a smart-home newcomer. Drop by on GitHub, and simply give it a try."],
];
for (const [id,text] of lines) {
  const r = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${VOICE}?output_format=mp3_44100_128`, {
    method:'POST', headers:{'xi-api-key':KEY,'Content-Type':'application/json'},
    body: JSON.stringify({ text, model_id:'eleven_v3', voice_settings:{stability:0.5,similarity_boost:0.75,use_speaker_boost:true} })
  });
  if(!r.ok){ console.log(id,'ERR',r.status,(await r.text()).slice(0,120)); continue; }
  fs.writeFileSync(`${OUT}/${id}.mp3`, Buffer.from(await r.arrayBuffer()));
  const dur = execSync(`ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "${OUT}/${id}.mp3"`).toString().trim();
  console.log(id, dur);
}
