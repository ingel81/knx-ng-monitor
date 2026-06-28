import fs from 'fs';
import { execSync } from 'child_process';
const KEY = fs.readFileSync(process.env.USERPROFILE + '/.claude/skills/video-use/.env','utf8').match(/ELEVENLABS_API_KEY=(.*)/)[1].trim();
const OUT='D:/Source/knx-ng-monitor/docs/screenshots/edit/vo';
fs.mkdirSync(OUT,{recursive:true});
const VOICE='dpsgxAAQscpwOkeRSVZr';
const lines = [
 ["s00","KNX-NG Monitor macht deinen KNX-Bus sichtbar. Jedes Telegramm in Echtzeit — automatisch mitgeschnitten, dauerhaft gespeichert und übersichtlich aufbereitet. Quelloffen, und komplett bei dir gehostet."],
 ["s01","Im Live-Monitor läuft der gesamte Bus-Verkehr durch, sobald er passiert. Die Verbindung bleibt dauerhaft bestehen und stellt sich nach einem Aussetzer von selbst wieder her. Jedes Telegramm wird sofort dekodiert — du siehst den echten Wert mit Einheit, statt nur roher Bytes."],
 ["s02","Dabei geht nichts verloren: Jedes Telegramm wandert lückenlos ins Archiv. Du durchsuchst den kompletten Verlauf per Volltext, filterst nach Adresse, Typ oder Raum — und das ganz ohne Mengenbegrenzung. Bei Bedarf exportierst du alles als Datei."],
 ["s03","Zu jedem Telegramm gibt es die volle Detailansicht, mit Wert, Zeitpunkt und Herkunft. Und du bleibst nicht nur Zuschauer: Werte lassen sich direkt aus dem Tool heraus auf den Bus schreiben oder gezielt auslesen."],
 ["s04","Zahlenwerte stellt der Monitor als Zeitreihen dar. Jede Einheit bekommt ihre eigene Achse, damit zum Beispiel Temperatur und Leistung auch gemeinsam lesbar bleiben. Neue Messwerte kommen live hinzu, während du zuschaust."],
 ["s05","Dabei spielt es keine Rolle, ob es um Messwerte oder um Schaltvorgänge geht. Temperatur, Helligkeit, Leistung oder einfache Ein-Aus-Zustände — alles landet im selben Diagramm und wird über die Zeit vergleichbar."],
 ["s06","Die Statistik liefert den Überblick: wie viele Telegramme insgesamt unterwegs waren, wie viele pro Sekunde im Schnitt, und wie sich das Aufkommen über die Zeit verteilt."],
 ["s07","Die Aktivitäts-Heatmap zeigt, wann auf dem Bus wirklich etwas los ist. Wochentag mal Stunde — so erkennst du Routinen und wiederkehrende Muster auf einen Blick."],
 ["s08","Dein bestehendes ETS-Projekt bringst du bequem per Assistent herein — egal ob ETS 4, 5 oder 6. Gruppenadressen, Geräte und Hardware werden automatisch übernommen, sodass alles gleich mit Klarnamen erscheint."],
 ["s09","Auch KNX Secure wird unterstützt. Passwortgeschützte Projekte, die Entschlüsselung über den Keyring, und Data Secure zur Laufzeit — verschlüsselte Telegramme werden also direkt lesbar gemacht."],
 ["s10","Die Topologie bildet deine Installation ab — vom Gebäude über die Etage bis zum einzelnen Raum. Dazu die Kommunikationsobjekte und ein Raumfilter, mit dem du dich gezielt auf einen Bereich konzentrierst."],
 ["s11","Alle Gruppenadressen findest du im durchsuchbaren Baum mit drei Ebenen. Von dort liest oder schreibst du jeden Wert direkt — oder öffnest ihn mit einem Klick als Diagramm."],
 ["s12","Und das Ganze ist voll responsive. Ob am Smartphone unterwegs oder am Tablet auf der Couch — die Oberfläche passt sich an und bleibt genauso bedienbar wie am Desktop."],
 ["s13","Ein helles Thema für tagsüber, ein dunkles für abgedunkelte Räume. Deutsch oder Englisch, jederzeit live umschaltbar — ganz nach deinem Geschmack."],
 ["s14","Laufen tut es praktisch überall: als Docker-Container oder als portable Anwendung, auf Linux, Windows und macOS — und dank ARM sogar auf dem Raspberry Pi. Bedient wird alles im Browser, und bleibt dabei komplett lokal bei dir."],
 ["s15","Noch experimentell, aber spannend: der Netzwerk-Graph der Gruppenadressen. Vom Gebäude über Etage und Raum bis zur einzelnen Adresse — und live eintreffende Telegramme lassen die passenden Knoten kurz aufleuchten."],
 ["s16","KNX-NG Monitor ist quelloffen und steht unter der MIT-Lizenz. Das Projekt sucht gerade Beta-Tester — egal ob KNX-Profi oder Smart-Home-Einsteiger. Schau auf GitHub vorbei und probier es einfach aus."],
];
for (const [id,text] of lines) {
  const r = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${VOICE}?output_format=mp3_44100_128`, {
    method:'POST', headers:{'xi-api-key':KEY,'Content-Type':'application/json'},
    body: JSON.stringify({ text, model_id:'eleven_multilingual_v2', voice_settings:{stability:0.45,similarity_boost:0.75,style:0.0,use_speaker_boost:true} })
  });
  if(!r.ok){ console.log(id,'ERR',r.status,(await r.text()).slice(0,120)); continue; }
  fs.writeFileSync(`${OUT}/${id}.mp3`, Buffer.from(await r.arrayBuffer()));
  const dur = execSync(`ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "${OUT}/${id}.mp3"`).toString().trim();
  console.log(id, dur);
}
