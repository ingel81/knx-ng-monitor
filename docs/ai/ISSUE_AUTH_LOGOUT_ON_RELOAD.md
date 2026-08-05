# Issue: Neuladen nach 15 Minuten Pause erzwingt Neuanmeldung

**Status:** behoben (Punkte 1, 2 und 4 des Fixvorschlags), Punkt 3 bewusst offen
**Gefunden:** 2026-07-28 · **Behoben:** 2026-08-05
**Betraf:** Produktion und Entwicklung gleichermassen — kein Dev-Artefakt

## Verifikation des Fixes

Mit abgelaufenem Access-Token und gültigem Refresh-Token im localStorage, dann
Neuladen auf `/monitor`:

| Szenario | Ergebnis |
|---|---|
| Access-Token abgelaufen, Refresh-Token gültig | bleibt auf `/monitor`, ein `POST /auth/refresh` → 200, `tokenExpiry` erneuert |
| kein Refresh-Token vorhanden | Login-Maske, wie gewünscht |

Vor dem Fix landete der erste Fall auf der Anmeldung, ohne dass je ein
Refresh-Aufruf stattfand.

## Symptom

Wer die Anwendung länger als 15 Minuten unberührt liegen lässt und dann neu lädt,
landet auf der Login-Maske, obwohl die Sitzung serverseitig noch 7 Tage gültig wäre.
Trifft F5, Browser-Neustart, wiederhergestellte Tabs und verdrängte Handy-Tabs.

Solange die Anwendung offen und aktiv ist, fällt es nicht auf: Der HTTP-Interceptor
und die SignalR-Verbindung erneuern den Token im laufenden Betrieb. Bei Ruhe feuert
keins von beidem.

## Ursache

`frontend/src/app/shared/guards/auth.guard.ts:10` entscheidet **synchron** über
`authService.isAuthenticated()` → `auth.service.ts:120-130`:

```ts
private hasValidToken(): boolean {
  const token = this.getAccessToken();
  const expiry = localStorage.getItem('tokenExpiry');
  if (!token || !expiry) { return false; }
  return new Date(expiry) > new Date();
}
```

`tokenExpiry` ist ausschliesslich der Ablauf des **Access-Tokens** (15 Minuten).
Der Refresh-Token (7 Tage) wird an dieser Stelle nie betrachtet, und es gibt keinen
zweiten Rettungsanker:

- kein `APP_INITIALIZER`/Bootstrap-Refresh (`app.config.ts:10-22`)
- der Interceptor erneuert nur als Reaktion auf ein 401 (`auth.interceptor.ts:29-31`),
  kommt aber nie dazu — der Guard navigiert weg, bevor die erste authentifizierte
  Anfrage rausgeht

Lebensdauern: `backend/KnxMonitor.Api/appsettings.json:17-18` — Access 15 min,
Refresh 7 Tage. Keine Overrides in `appsettings.Development.json`, also in beiden
Umgebungen gleich.

## Ausgeschlossen (jeweils belegt)

| Verdacht | Befund |
|---|---|
| Dev-Proxy / relative API-Pfade | Login, Refresh und authentifizierte Anfragen laufen über :4200 und :8080 identisch (200/401 wie erwartet) |
| Gegenseitiges Abmelden mehrerer Nutzer | `RevokeAllTokensAsync` filtert strikt auf `UserId` (`AuthService.cs:127-140`) |
| API-Neustart mit neuem JWT-Secret | Prozess lief während aller Rauswürfe durchgehend |
| Token-Cleanup-Worker | löscht nur abgelaufene bzw. länger als 7 Tage revokierte Zeilen (`RefreshTokenCleanupService.cs:34-36`) |

Belegend auch die Datenbank: 28 Refresh-Tokens für einen Nutzer in zwei Tagen,
davon nur 6 aus echten Rotationen — der Rest sind Neuanmeldungen.

## Fix

Umgesetzt sind 1, 2 und 4; Punkt 3 bleibt offen (siehe Begründung dort).

1. **Kern:** Guard asynchron machen. Bei abgelaufenem Access-Token, aber vorhandenem
   Refresh-Token erst `refreshToken()` abwarten und nur bei dessen Fehlschlag nach
   `/login` navigieren. Alternativ `APP_INITIALIZER`, damit auch SignalR und die
   ersten Feature-Anfragen hinter einem gültigen Token starten.
2. `clearSession()` (`auth.interceptor.ts:42-47`) nur bei echtem 401 auslösen —
   aktuell verwirft jeder fehlgeschlagene Refresh die Sitzung, auch bei Netzwerkfehler
   (Status 0) oder 502.
3. **Offen —** Reuse-Detection (`AuthService.cs:73-77`) um eine kurze Gnadenfrist erweitern.
   Braucht ein `RevokedAt` an der `RefreshToken`-Entität plus Migration; der Race wird
   durch den Guard-Fix ohnehin seltener, weil deutlich weniger Refreshes anfallen.
   Ursprüngliche Beschreibung:
   Ein Reload mitten im laufenden Refresh verwirft die Antwort, localStorage behält
   den bereits rotierten Token — der nächste Versuch entwertet dann alle Sitzungen
   des Nutzers. Live reproduziert.
4. `ClockSkew` ist in `Program.cs:147-157` nicht gesetzt (Default 5 min): Der Server
   akzeptiert den Token faktisch 20 Minuten, das Frontend hält sich schon nach 15 für
   abgemeldet. Die Client-Prüfung ist damit strenger als die Server-Prüfung.

## Grundsätzliche Alternative

Umstellung der Web-Oberfläche auf ein `HttpOnly`-Cookie statt Bearer-Token im
`localStorage`. In Produktion liefert dasselbe Backend das Frontend aus `wwwroot`
aus, also gleiche Herkunft — der Fall, für den Cookies gebaut sind. Der Browser
übernimmt Speicherung, Mitsenden und Ablauf; die manuelle Refresh-Mechanik entfällt
und dieser Fehler kann in der Form nicht entstehen. Zusätzlich XSS-sicherer, weil
für Skripte unlesbar, und SignalR bräuchte den Token nicht mehr im Query-String.

Preis: CSRF muss adressiert werden (`SameSite=Lax` plus Anti-Forgery-Token auf den
schreibenden Endpunkten — hier besonders relevant wegen `/api/knx/write`). Für
Zugriffe ausserhalb des Browsers sollte Bearer parallel erhalten bleiben.

Eigenes Vorhaben mit Backend-, SignalR- und Frontend-Anteil; meldet beim Ausrollen
alle bestehenden Sitzungen ab.

## Nebenbefund

`frontend/angular.json:70-81` verdrahtet `proxyConfig` nicht. Ein `ng serve` ohne
`--proxy-config proxy.conf.json` schickt alle `/api`-Aufrufe an den Dev-Server, der
`index.html` statt JSON liefert. Ein `"proxyConfig": "proxy.conf.json"` in der
`development`-Serve-Konfiguration macht das robust.
