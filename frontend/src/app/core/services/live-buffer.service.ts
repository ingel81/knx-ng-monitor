import { Injectable, inject } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { SignalrService, KnxTelegram } from './signalr.service';

/**
 * App-weiter Live-Telegramm-Buffer (Hot-Tier im Frontend).
 *
 * Hält die letzten N Telegramme als Singleton — überlebt damit den
 * Lebenszyklus der LiveViewComponent. Beim Tab-Wechsel History→Live bleibt
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

  get telegrams(): KnxTelegram[] { return this.buffer; }
  get isPaused(): boolean { return this.paused; }

  /** Startet die SignalR-Verbindung + Subscription genau einmal (idempotent). */
  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    await this.signalr.startConnection();
    this.signalr.telegram$.subscribe((t) => this.onTelegram(t));
  }

  private onTelegram(t: KnxTelegram): void {
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
