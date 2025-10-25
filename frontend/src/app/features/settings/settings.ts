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
      const configs = await this.http.get<KnxConfiguration[]>('http://localhost:5075/api/knx/configurations').toPromise();

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

  async saveSettings() {
    try {
      this.isSaving = true;

      // Save to backend database
      const configs = await this.http.get<KnxConfiguration[]>('http://localhost:5075/api/knx/configurations').toPromise();

      if (configs && configs.length > 0) {
        // Update existing configuration
        await this.http.put(`http://localhost:5075/api/knx/configurations/${configs[0].id}`, {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: 0 // Tunneling
        }).toPromise();
      } else {
        // Create new configuration
        await this.http.post('http://localhost:5075/api/knx/configurations', {
          ipAddress: this.knxConfig.ipAddress,
          port: this.knxConfig.port,
          physicalAddress: this.knxConfig.physicalAddress,
          connectionType: 0 // Tunneling
        }).toPromise();
      }

      this.snackBar.open('Settings saved successfully to database', 'Close', {
        duration: 3000,
        horizontalPosition: 'end',
        verticalPosition: 'top'
      });
    } catch (error) {
      console.error('Failed to save settings:', error);
      this.snackBar.open('Failed to save settings', 'Close', {
        duration: 3000,
        horizontalPosition: 'end',
        verticalPosition: 'top'
      });
    } finally {
      this.isSaving = false;
    }
  }

  async testConnection() {
    try {
      this.isTesting = true;

      // First save the settings
      await this.saveSettings();

      // Get the configuration
      const configs = await this.http.get<KnxConfiguration[]>('http://localhost:5075/api/knx/configurations').toPromise();

      if (!configs || configs.length === 0) {
        throw new Error('No configuration found');
      }

      // Try to connect
      await this.http.post('http://localhost:5075/api/knx/connect', configs[0].id).toPromise();

      this.snackBar.open('✓ Connection successful!', 'Close', {
        duration: 3000,
        horizontalPosition: 'end',
        verticalPosition: 'top',
        panelClass: ['success-snackbar']
      });

      // Disconnect after test
      setTimeout(async () => {
        await this.http.post('http://localhost:5075/api/knx/disconnect', {}).toPromise();
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
}
