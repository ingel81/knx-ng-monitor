import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { SHEET_MOBILE_BREAKPOINT, TelegramDetailComponent, TelegramDetailData } from './telegram-detail.component';

/**
 * Oeffnet das Telegramm-Detail-Sheet: Desktop -> rechts angedocktes Panel,
 * Mobil (<768px) -> Bottom-Sheet. Auswahl per Viewport zur Oeffnungszeit.
 */
@Injectable({ providedIn: 'root' })
export class TelegramDetailService {
  private dialog = inject(MatDialog);

  open(data: TelegramDetailData): void {
    const isMobile = window.innerWidth < SHEET_MOBILE_BREAKPOINT;
    this.dialog.open(TelegramDetailComponent, {
      data,
      autoFocus: false,
      panelClass: isMobile ? 'knx-sheet-panel-bottom' : 'knx-sheet-panel-right',
      position: isMobile ? { bottom: '0', left: '0' } : { right: '0', top: '0' },
      width: isMobile ? '100vw' : '400px',
      maxWidth: '100vw',
      height: isMobile ? 'auto' : '100vh',
      // dvh, nicht vh: Android Chrome rechnet vh gegen den largest viewport (ohne Adressleiste),
      // 85vh sind sichtbar also ~100 % -> das Sheet wirkt wie Vollbild.
      maxHeight: isMobile ? '85dvh' : '100vh',
    });
  }
}
