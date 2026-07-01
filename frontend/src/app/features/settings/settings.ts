import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { environment } from '../../../environments/environment.development';
import { RecordingSettingsService } from '../../core/services/recording-settings.service';
import { RecordingSettings } from '../../core/models/recording-settings.models';
import { ThemeService, ThemeMode, Density } from '../../core/services/theme.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog.component';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoggerService } from '../../core/logging/logger.service';
import { DiagnosticsService } from '../../core/services/diagnostics.service';

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

interface KnxSettings {
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

@Component({
  selector: 'app-settings',
  imports: [CommonModule, FormsModule, MatIconModule, MatSnackBarModule, MatDialogModule, MatTooltipModule, TranslatePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  private http = inject(HttpClient);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private recordingService = inject(RecordingSettingsService);
  private themeService = inject(ThemeService);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);
  private diagnostics = inject(DiagnosticsService);

  isDownloadingDiagnostics = false;

  // Erscheinungsbild (Theme + Dichte), aus ThemeService gespiegelt
  readonly theme = this.themeService.theme;
  readonly density = this.themeService.density;
  setTheme(mode: ThemeMode): void { this.themeService.setTheme(mode); }
  setDensity(d: Density): void { this.themeService.setDensity(d); }

  knxConfig: KnxSettings = {
    ipAddress: '192.168.10.60',
    port: 3671,
    physicalAddress: '1.0.58'
  };

  isTesting = false;
  isSaving = false;

  // Gateway auto-connect: whether the auto-connect worker keeps the bus link up
  // automatically (initial connect + reconnect after drops). Pure connection policy,
  // independent of any project. Backed by KnxConfiguration.AutoConnect.
  autoConnect = true;
  autoConnectBusy = false;

  recording: RecordingSettings = {
    hotBufferMaxCount: 1_000_000,
    archiveEnabled: false,
    archiveRetentionDays: null
  };
  isSavingRecording = false;

  ngOnInit() {
    this.loadSettings();
    this.loadRecordingSettings();
    this.loadAutoConnect();
  }

  async loadAutoConnect() {
    try {
      const state = await this.http
        .get<{ enabled: boolean }>(`${environment.apiUrl}/knx/autoconnect`)
        .toPromise();
      if (state) {
        this.autoConnect = state.enabled;
      }
    } catch (error) {
      this.logger.error('Failed to load auto-connect state:', error);
    }
  }

  async toggleAutoConnect() {
    this.autoConnectBusy = true;
    const next = !this.autoConnect;
    try {
      const state = await this.http
        .put<{ enabled: boolean }>(`${environment.apiUrl}/knx/autoconnect`, { enabled: next })
        .toPromise();
      this.autoConnect = state?.enabled ?? next;
      this.snackBar.open(
        this.lang.translate(this.autoConnect ? 'settings.autoConnectEnabled' : 'settings.autoConnectDisabled'),
        this.lang.translate('common.close'),
        { duration: 2500, horizontalPosition: 'end', verticalPosition: 'top' });
    } catch (error) {
      this.logger.error('Failed to set auto-connect:', error);
      this.snackBar.open(
        this.lang.translate('settings.autoConnectChangeFailed'),
        this.lang.translate('common.close'),
        { duration: 3000, horizontalPosition: 'end', verticalPosition: 'top', panelClass: ['error-snackbar'] });
    } finally {
      this.autoConnectBusy = false;
    }
  }

  async loadRecordingSettings() {
    try {
      const settings = await this.recordingService.getSettings().toPromise();
      if (settings) {
        this.recording = settings;
      }
    } catch (error) {
      this.logger.error('Failed to load recording settings:', error);
    }
  }

  async saveRecordingSettings() {
    if (!this.isRecordingFormValid()) {
      return;
    }
    try {
      this.isSavingRecording = true;
      const updated = await this.recordingService.updateSettings(this.recording).toPromise();
      if (updated) {
        this.recording = updated;
      }
      this.snackBar.open(this.lang.translate('settings.recordingApplied'), this.lang.translate('common.close'), {
        duration: 3000,
        horizontalPosition: 'end',
        verticalPosition: 'top',
        panelClass: ['success-snackbar']
      });
    } catch (error) {
      this.logger.error('Failed to save recording settings:', error);
      this.snackBar.open(this.lang.translate('settings.recordingSaveFailed'), this.lang.translate('common.close'), {
        duration: 3000,
        horizontalPosition: 'end',
        verticalPosition: 'top',
        panelClass: ['error-snackbar']
      });
    } finally {
      this.isSavingRecording = false;
    }
  }

  isRecordingFormValid(): boolean {
    if (!this.recording.hotBufferMaxCount || this.recording.hotBufferMaxCount < 1) {
      return false;
    }
    if (this.recording.archiveEnabled &&
        this.recording.archiveRetentionDays !== null &&
        this.recording.archiveRetentionDays < 1) {
      return false;
    }
    return true;
  }

  async loadSettings() {
    try {
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();

      if (configs && configs.length > 0) {
        const config = configs[0];
        this.knxConfig = {
          ipAddress: config.ipAddress,
          port: config.port,
          physicalAddress: config.physicalAddress
        };
      }
    } catch (error) {
      this.logger.error('Failed to load settings:', error);
    }
  }

  async saveSettings(showToast: boolean = true) {
    try {
      this.isSaving = true;

      // Save to backend database
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();

      if (configs && configs.length > 0) {
        // Update existing configuration
        await this.http.put(`${environment.apiUrl}/knx/configurations/${configs[0].id}`, {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: 0, // Tunneling
          autoConnect: this.autoConnect
        }).toPromise();
      } else {
        // Create new configuration
        await this.http.post(`${environment.apiUrl}/knx/configurations`, {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: 0, // Tunneling
          autoConnect: this.autoConnect
        }).toPromise();
      }

      if (showToast) {
        this.snackBar.open(this.lang.translate('settings.saved'), this.lang.translate('common.close'), {
          duration: 3000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['success-snackbar']
        });
      }
    } catch (error) {
      this.logger.error('Failed to save settings:', error);
      if (showToast) {
        this.snackBar.open(this.lang.translate('settings.saveFailed'), this.lang.translate('common.close'), {
          duration: 3000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['error-snackbar']
        });
      }
      throw error; // Re-throw to handle in testConnection
    } finally {
      this.isSaving = false;
    }
  }

  async testConnection() {
    try {
      // If a live link is up, the probe briefly pauses it (frees the tunnel). Warn first.
      const status = await this.http
        .get<{ isConnected: boolean }>(`${environment.apiUrl}/knx/status`)
        .toPromise();

      if (status?.isConnected) {
        const data: ConfirmDialogData = {
          title: this.lang.translate('settings.testTitle'),
          message: this.lang.translate('settings.testMsg'),
          confirmText: this.lang.translate('settings.testConfirm'),
          cancelText: this.lang.translate('common.cancel')
        };
        const confirmed = await this.dialog
          .open(ConfirmDialogComponent, { data, width: '440px' })
          .afterClosed()
          .toPromise();
        if (!confirmed) {
          return;
        }
      }

      this.isTesting = true;

      // Persist the entered settings first (silent)
      await this.saveSettings(false);

      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();
      if (!configs || configs.length === 0) {
        throw new Error('No configuration found');
      }

      // Non-destructive probe: does NOT touch the live recording connection.
      const result = await this.http
        .post<{ success: boolean }>(`${environment.apiUrl}/knx/test-connection`, configs[0].id)
        .toPromise();

      if (result?.success) {
        this.snackBar.open(this.lang.translate('settings.testSuccess'), this.lang.translate('common.close'), {
          duration: 4000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['success-snackbar']
        });
      } else {
        this.snackBar.open(this.lang.translate('settings.testFailed'), this.lang.translate('common.close'), {
          duration: 5000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['error-snackbar']
        });
      }
    } catch (error) {
      this.logger.error('Connection test failed:', error);
      this.snackBar.open(this.lang.translate('settings.testFailed'), this.lang.translate('common.close'), {
        duration: 5000,
        horizontalPosition: 'end',
        verticalPosition: 'top',
        panelClass: ['error-snackbar']
      });
    } finally {
      this.isTesting = false;
    }
  }

  resetToDefaults() {
    this.knxConfig = {
      ipAddress: '192.168.10.60',
      port: 3671,
      physicalAddress: '1.0.58'
    };
    this.saveSettings();
  }

  downloadDiagnostics(): void {
    if (this.isDownloadingDiagnostics) return;
    this.isDownloadingDiagnostics = true;
    this.diagnostics.downloadDiagnostics().subscribe({
      next: blob => {
        const ts = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `knx-diagnostics-${ts}.zip`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        this.isDownloadingDiagnostics = false;
      },
      error: err => {
        this.logger.error('Diagnostics download failed:', err);
        this.snackBar.open(this.lang.translate('logs.downloadFailed'), this.lang.translate('common.close'), {
          duration: 4000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['error-snackbar']
        });
        this.isDownloadingDiagnostics = false;
      }
    });
  }

  isFormValid(): boolean {
    // IP Address validation
    const ipPattern = /^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$/;
    if (!ipPattern.test(this.knxConfig.ipAddress)) {
      return false;
    }

    // Port validation
    if (!this.knxConfig.port || this.knxConfig.port < 1 || this.knxConfig.port > 65535) {
      return false;
    }

    // Physical Address validation
    const paPattern = /^\d{1,2}\.\d{1,2}\.\d{1,3}$/;
    if (!paPattern.test(this.knxConfig.physicalAddress)) {
      return false;
    }

    return true;
  }
}
