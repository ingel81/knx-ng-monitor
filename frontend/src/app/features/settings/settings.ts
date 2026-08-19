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
import { Availability, AvailabilityInterval } from '../../core/models/availability.models';
import { localeTag } from '../../core/i18n/locale.util';
import { formatDurationMinutes } from '../../shared/duration.util';

/**
 * Mirrors Core.Enums.ConnectionType. The API serialises enums as strings
 * (JsonStringEnumConverter), so the wire format is the enum name — older
 * clients sent the ordinal, which the converter still accepts on input.
 */
type ConnectionMode = 'Tunneling' | 'Routing';

/** Default KNXnet/IP routing group per the KNX standard. */
const ROUTING_MULTICAST_DEFAULT = '224.0.23.12';
const TUNNELING_IP_DEFAULT = '192.168.10.60';

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
  connectionType: ConnectionMode | number;
}

interface KnxSettings {
  ipAddress: string;
  port: number;
  physicalAddress: string;
  connectionType: ConnectionMode;
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

  /** Availability of the last few days — explains holes in the archive. Null until loaded. */
  availability: Availability | null = null;
  isLoadingAvailability = false;
  availabilityFailed = false;

  // Erscheinungsbild (Theme + Dichte), aus ThemeService gespiegelt
  readonly theme = this.themeService.theme;
  readonly density = this.themeService.density;
  setTheme(mode: ThemeMode): void { this.themeService.setTheme(mode); }
  setDensity(d: Density): void { this.themeService.setDensity(d); }

  knxConfig: KnxSettings = {
    ipAddress: TUNNELING_IP_DEFAULT,
    port: 3671,
    physicalAddress: '1.0.58',
    connectionType: 'Tunneling'
  };

  /** Per-mode endpoint memory; see setConnectionType(). */
  private endpointDrafts: Record<ConnectionMode, { ipAddress: string; port: number }> = {
    Tunneling: { ipAddress: TUNNELING_IP_DEFAULT, port: 3671 },
    Routing: { ipAddress: ROUTING_MULTICAST_DEFAULT, port: 3671 }
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
    this.loadAvailability();
  }

  /** Default window of the availability panel; matches the backend default. */
  private static readonly AvailabilityDays = 7;

  loadAvailability(): void {
    this.isLoadingAvailability = true;
    this.availabilityFailed = false;
    const to = new Date();
    const from = new Date(to.getTime() - Settings.AvailabilityDays * 24 * 60 * 60 * 1000);
    this.diagnostics.getAvailability(from.toISOString(), to.toISOString()).subscribe({
      next: result => {
        this.availability = result;
        this.isLoadingAvailability = false;
      },
      error: err => {
        this.logger.error('Availability load failed:', err);
        this.availabilityFailed = true;
        this.isLoadingAvailability = false;
      }
    });
  }

  /** Outages worth showing, longest first — a two-minute blip is not the headline. */
  get outages(): AvailabilityInterval[] {
    return [...(this.availability?.outages ?? [])].sort((a, b) => b.minutes - a.minutes);
  }

  /** Zeit ohne Aufzeichnung (vor Beginn der Heartbeats). Kein Ausfall, aber auch keine Entwarnung. */
  get unknownMinutes(): number {
    return this.availability?.unknownMinutes ?? 0;
  }

  outageReason(interval: AvailabilityInterval): string {
    return this.lang.translate(
      interval.state === 'MonitorDown' ? 'availability.monitorDown' : 'availability.busDown');
  }

  outageRange(interval: AvailabilityInterval): string {
    return this.lang.translate('availability.range', {
      from: this.formatInstant(interval.from),
      to: this.formatInstant(interval.to)
    });
  }

  formatDuration(minutes: number): string {
    return formatDurationMinutes(minutes);
  }

  private formatInstant(iso: string): string {
    return new Date(iso).toLocaleString(localeTag(this.lang.lang()), {
      day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
    });
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
          physicalAddress: config.physicalAddress,
          connectionType: this.toConnectionMode(config.connectionType)
        };
        this.endpointDrafts[this.knxConfig.connectionType] = {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port
        };
      }
    } catch (error) {
      this.logger.error('Failed to load settings:', error);
    }
  }

  /** Accepts both the string enum name and the legacy ordinal. */
  private toConnectionMode(value: ConnectionMode | number | undefined): ConnectionMode {
    return value === 'Routing' || value === 1 ? 'Routing' : 'Tunneling';
  }

  get isRouting(): boolean {
    return this.knxConfig.connectionType === 'Routing';
  }

  /**
   * Address and port mean different things per mode — a gateway IP for tunneling, a
   * multicast group for routing — so each mode keeps its own draft. Toggling the
   * switch back and forth therefore never destroys a typed-in gateway address or a
   * custom port, which a plain "overwrite with the default" would.
   */
  setConnectionType(mode: ConnectionMode): void {
    if (this.knxConfig.connectionType === mode) {
      return;
    }

    this.endpointDrafts[this.knxConfig.connectionType] = {
      ipAddress: this.knxConfig.ipAddress,
      port: this.knxConfig.port
    };

    const draft = this.endpointDrafts[mode];
    this.knxConfig.connectionType = mode;
    this.knxConfig.ipAddress = draft.ipAddress;
    this.knxConfig.port = draft.port;
  }

  /** True for the IPv4 multicast range 224.0.0.0 – 239.255.255.255. */
  private isMulticastAddress(ip: string): boolean {
    const first = Number(ip.split('.')[0]);
    return Number.isFinite(first) && first >= 224 && first <= 239;
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
          connectionType: this.knxConfig.connectionType,
          autoConnect: this.autoConnect
        }).toPromise();
      } else {
        // Create new configuration
        await this.http.post(`${environment.apiUrl}/knx/configurations`, {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: this.knxConfig.connectionType,
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
        .post<{ success: boolean; outcome: string }>(`${environment.apiUrl}/knx/test-connection`, configs[0].id)
        .toPromise();

      // Routing joined the group but saw no telegrams — the socket is fine, so this is neither a
      // success nor a failure. Most likely multicast never reaches this process.
      if (result?.outcome === 'JoinedWithoutTraffic') {
        this.snackBar.open(this.lang.translate('settings.testNoTraffic'), this.lang.translate('common.close'), {
          duration: 8000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['warn-snackbar']
        });
      } else if (result?.success) {
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
      ipAddress: TUNNELING_IP_DEFAULT,
      port: 3671,
      physicalAddress: '1.0.58',
      connectionType: 'Tunneling'
    };
    this.endpointDrafts = {
      Tunneling: { ipAddress: TUNNELING_IP_DEFAULT, port: 3671 },
      Routing: { ipAddress: ROUTING_MULTICAST_DEFAULT, port: 3671 }
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

    // The address means different things per mode, and the two ranges are disjoint:
    // routing joins a multicast group, tunneling opens a unicast connection to a single
    // gateway. A mismatch only surfaces as a failed connect buried in the log.
    if (this.isRouting !== this.isMulticastAddress(this.knxConfig.ipAddress)) {
      return false;
    }

    // Port validation
    if (!this.knxConfig.port || this.knxConfig.port < 1 || this.knxConfig.port > 65535) {
      return false;
    }

    // The physical address only matters for routing, where it is the source address we send
    // under; the field is hidden for tunneling, so validating it there would disable Save with
    // nothing on screen to explain why. Out of range the backend cannot parse it and silently
    // falls back to a default source, hence the strict range check on top of the format.
    if (this.isRouting) {
      const paPattern = /^\d{1,2}\.\d{1,2}\.\d{1,3}$/;
      if (!paPattern.test(this.knxConfig.physicalAddress)
          || !this.isIndividualAddress(this.knxConfig.physicalAddress)) {
        return false;
      }
    }

    return true;
  }

  /** KNX individual address ranges: area 0-15, line 0-15, device 0-255. */
  private isIndividualAddress(value: string): boolean {
    const parts = value.split('.').map(Number);
    return parts.length === 3
      && parts.every(Number.isInteger)
      && parts[0] >= 0 && parts[0] <= 15
      && parts[1] >= 0 && parts[1] <= 15
      && parts[2] >= 0 && parts[2] <= 255;
  }
}
