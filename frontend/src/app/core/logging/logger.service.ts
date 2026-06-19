// NOTE: This is the ONLY place in src/app/** allowed to call console.*.
// All application code must inject LoggerService and use its methods instead.
// (No ESLint is configured in this project, so this convention is enforced by
//  review/convention rather than a no-console lint rule.)
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class LoggerService {
  private readonly isDev = !environment.production;

  /** Verbose debug output. Suppressed in production. */
  debug(message: unknown, ...args: unknown[]): void {
    if (this.isDev) {
      console.log(message, ...args);
    }
  }

  /** Informational output. Suppressed in production. */
  info(message: unknown, ...args: unknown[]): void {
    if (this.isDev) {
      console.info(message, ...args);
    }
  }

  /** Warnings. Always emitted. */
  warn(message: unknown, ...args: unknown[]): void {
    console.warn(message, ...args);
  }

  /** Errors. Always emitted. */
  error(message: unknown, ...args: unknown[]): void {
    console.error(message, ...args);
  }
}
