import { Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';
export type Density = 'compact' | 'cozy';

const THEME_KEY = 'knx.theme';
const DENSITY_KEY = 'knx.density';

/**
 * Globales Erscheinungsbild: Theme (Light/Console-Dark) + Grid-Dichte.
 * Persistiert in localStorage. Theme schaltet die Klasse `theme-dark` auf
 * <html>; die CSS-Custom-Properties (styles/_tokens.scss) folgen automatisch.
 * Dichte schaltet `density-cozy` auf <html> (Default: compact).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<ThemeMode>(this.readTheme());
  readonly density = signal<Density>(this.readDensity());

  /** Beim App-Start aufrufen — wendet den persistierten Zustand an. */
  init(): void {
    this.applyTheme(this.theme());
    this.applyDensity(this.density());
  }

  setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
    localStorage.setItem(THEME_KEY, mode);
    this.applyTheme(mode);
  }

  toggleTheme(): void {
    this.setTheme(this.theme() === 'dark' ? 'light' : 'dark');
  }

  setDensity(density: Density): void {
    this.density.set(density);
    localStorage.setItem(DENSITY_KEY, density);
    this.applyDensity(density);
  }

  private applyTheme(mode: ThemeMode): void {
    const root = document.documentElement;
    root.classList.toggle('theme-dark', mode === 'dark');
  }

  private applyDensity(density: Density): void {
    const root = document.documentElement;
    root.classList.toggle('density-cozy', density === 'cozy');
  }

  private readTheme(): ThemeMode {
    return localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light';
  }

  private readDensity(): Density {
    return localStorage.getItem(DENSITY_KEY) === 'cozy' ? 'cozy' : 'compact';
  }
}
