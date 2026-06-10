import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { TelegramDetailComponent, TelegramDetailData } from './telegram-detail.component';

/**
 * Oeffnet das Telegramm-Detail-Sheet: Desktop -> rechts angedocktes Panel,
 * Mobil (<768px) -> Bottom-Sheet. Auswahl per Viewport zur Oeffnungszeit.
 */
@Injectable({ providedIn: 'root' })
export class TelegramDetailService {
  private dialog = inject(MatDialog);

  open(data: TelegramDetailData): void {
    const isMobile = window.innerWidth < 768;
    this.dialog.open(TelegramDetailComponent, {
      data,
      autoFocus: false,
      panelClass: isMobile ? 'knx-sheet-panel-bottom' : 'knx-sheet-panel-right',
      position: isMobile ? { bottom: '0', left: '0' } : { right: '0', top: '0' },
      width: isMobile ? '100vw' : '400px',
      maxWidth: '100vw',
      height: isMobile ? 'auto' : '100vh',
      maxHeight: isMobile ? '85vh' : '100vh',
    });
  }
}
