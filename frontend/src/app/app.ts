import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/services/theme.service';
import { KnxIconRegistry } from './core/services/knx-icon-registry.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('frontend');
  private readonly theme = inject(ThemeService);
  private readonly icons = inject(KnxIconRegistry);

  constructor() {
    this.theme.init();
    this.icons.register();
  }
}
