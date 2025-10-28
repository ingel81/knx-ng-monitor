import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { environment } from '../../../environments/environment.development';

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
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule
  ],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  private http = inject(HttpClient);
  private snackBar = inject(MatSnackBar);

  knxConfig: KnxSettings = {
    ipAddress: '192.168.10.60',
    port: 3671,
    physicalAddress: '1.0.58'
  };

  isTesting = false;
  isSaving = false;

  ngOnInit() {
    this.loadSettings();
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
      console.error('Failed to load settings:', error);
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
          connectionType: 0 // Tunneling
        }).toPromise();
      } else {
        // Create new configuration
        await this.http.post(`${environment.apiUrl}/knx/configurations`, {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: 0 // Tunneling
        }).toPromise();
      }

      if (showToast) {
        this.snackBar.open('✓ Settings saved successfully', 'Close', {
          duration: 3000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['success-snackbar']
        });
      }
    } catch (error) {
      console.error('Failed to save settings:', error);
      if (showToast) {
        this.snackBar.open('✗ Failed to save settings', 'Close', {
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
      this.isTesting = true;

      // First save the settings silently
      await this.saveSettings(false);

      // Get the configuration
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();

      if (!configs || configs.length === 0) {
        throw new Error('No configuration found');
      }

      // Try to connect
      await this.http.post(`${environment.apiUrl}/knx/connect`, configs[0].id).toPromise();

      this.snackBar.open('✓ Settings saved and connection successful!', 'Close', {
        duration: 4000,
        horizontalPosition: 'end',
        verticalPosition: 'top',
        panelClass: ['success-snackbar']
      });

      // Disconnect after test
      setTimeout(async () => {
        await this.http.post(`${environment.apiUrl}/knx/disconnect`, {}).toPromise();
      }, 1000);

    } catch (error) {
      console.error('Connection test failed:', error);
      this.snackBar.open('✗ Connection failed. Please check your settings.', 'Close', {
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
