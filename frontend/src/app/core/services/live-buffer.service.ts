import { Injectable, inject } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { SignalrService, KnxTelegram } from './signalr.service';

/**
 * App-weiter Live-Telegramm-Buffer (Hot-Tier im Frontend).
 *
 * Hält die letzten N Telegramme als Singleton — überlebt damit den
 * Lebenszyklus der MonitorComponent. Beim Wechsel Archive→Live bleibt
 * der Buffer erhalten (kein Neuaufbau, keine leere Ansicht), und die
 * SignalR-Verbindung wird NICHT gestoppt.
 *
 * Pause: ankommende Telegramme werden zwischengespeichert (stash) statt
 * verworfen und bei resume() in korrekter Reihenfolge nachgezogen.
 */
@Injectable({ providedIn: 'root' })
export class LiveBufferService {
  private signalr = inject(SignalrService);

  private readonly max = 1000;
  private buffer: KnxTelegram[] = [];     // newest-first
  private stash: KnxTelegram[] = [];      // während Pause angesammelt, newest-first
  private clientSeq = 0;
  private paused = false;
  private started = false;

  private changed = new Subject<void>();
  /** Emit bei jeder Buffer-Änderung (neues Telegramm, Pause-Flush, Clear). */
  readonly changed$: Observable<void> = this.changed.asObservable();

  // --- Bus-Last / Rate (gleitendes Fenster) ---------------------------------
  // Ankunftszeitpunkte (epoch ms) der letzten Telegramme; unabhängig von Pause,
  // da die Aufzeichnung im Hintergrund weiterläuft. Fenster = RATE_WINDOW_MS.
  private static readonly RATE_WINDOW_MS = 5000;
  // KNX TP1 schafft theoretisch ~50 Telegramme/s -> Referenz für die Bus-Last%.
  private static readonly TP1_MAX_TPS = 50;
  private arrivals: number[] = [];
  private rate = new Subject<void>();
  /** Tickt ~1×/s (und bei jeder Ankunft) — für die Rate-/Bus-Last-Anzeige. */
  readonly rate$: Observable<void> = this.rate.asObservable();
  private rateTimer?: ReturnType<typeof setInterval>;

  get telegrams(): KnxTelegram[] { return this.buffer; }
  get isPaused(): boolean { return this.paused; }

  /** Gleitende Telegramme/Sekunde über das Rate-Fenster. */
  get messagesPerSecond(): number {
    this.pruneArrivals();
    return this.arrivals.length / (LiveBufferService.RATE_WINDOW_MS / 1000);
  }

  /** Bus-Last in % der theoretischen TP1-Bandbreite (~50 tel/s), 0..100. */
  get busLoadPercent(): number {
    const pct = (this.messagesPerSecond / LiveBufferService.TP1_MAX_TPS) * 100;
    return Math.min(100, Math.round(pct));
  }

  private pruneArrivals(now = Date.now()): void {
    const cutoff = now - LiveBufferService.RATE_WINDOW_MS;
    let i = 0;
    while (i < this.arrivals.length && this.arrivals[i] < cutoff) i++;
    if (i > 0) this.arrivals.splice(0, i);
  }

  /** Startet die SignalR-Verbindung + Subscription genau einmal (idempotent). */
  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    await this.signalr.startConnection();
    this.signalr.telegram$.subscribe((t) => this.onTelegram(t));
    // Rate auch ohne neue Telegramme abklingen lassen (Anzeige aktuell halten).
    this.rateTimer = setInterval(() => { this.pruneArrivals(); this.rate.next(); }, 1000);
  }

  private onTelegram(t: KnxTelegram): void {
    // Ankunft fürs Rate-Fenster zählen (vor Pause-Check — Aufzeichnung läuft weiter).
    this.arrivals.push(Date.now());
    this.rate.next();

    // Live-Telegramme kommen vor dem Persistieren -> id=0. Eindeutige Client-Sequenz
    // vergeben (stabil pro Zeile) -> Zebra + trackBy.
    if (!t.id) t.id = ++this.clientSeq;

    if (this.paused) {
      this.stash.unshift(t);
      if (this.stash.length > this.max) this.stash.length = this.max;
      return;
    }
    this.buffer = [t, ...this.buffer];
    if (this.buffer.length > this.max) this.buffer.length = this.max;
    this.changed.next();
  }

  togglePause(): void { this.paused ? this.resume() : this.pause(); }

  pause(): void {
    this.paused = true;
    this.changed.next();
  }

  resume(): void {
    if (this.stash.length) {
      this.buffer = [...this.stash, ...this.buffer];
      if (this.buffer.length > this.max) this.buffer.length = this.max;
      this.stash = [];
    }
    this.paused = false;
    this.changed.next();
  }

  clear(): void {
    this.buffer = [];
    this.stash = [];
    this.changed.next();
  }
}
