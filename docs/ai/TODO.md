# TODO

Konsolidierter Stand: siehe [`PROJECT_PLAN.md`](PROJECT_PLAN.md) → "Nächste Schritte"
und das Highlights-Kapitel in [`../../README.md`](../../README.md) → "Project status".

## Kurz

### Parser / KNX Secure
- [ ] Telegramm-Decryption zur Laufzeit (KNX Data Secure am Bus, mit den
      gespeicherten ToolKeys / GA-Keys aus dem Keyring)
- [ ] Communication Objects, Topology, Locations, Functions parsen
- [ ] Frontend: Keyring nachträglich pro Projekt hochladen / aktualisieren

### UI / UX
- [ ] Filter-Chips + speicherbare Filter-Presets (Live View)
- [ ] Telegramm-Detail-Drawer + Vergleich zweier Zeitpunkte
- [ ] Mobile-Ansicht systematisch optimieren
- [ ] Accessibility-Audit (WCAG 2.1 Level AA)

### Betrieb / Qualität
- [ ] Health-Check-Endpoint
- [ ] Logging-Cleanup
  - Backend: Serilog-Templates vereinheitlichen
  - `Console.WriteLine` aus `ProjectImportService` durch `ILogger` ersetzen
  - Frontend: vorhandene `console.log`-Statements aufräumen
- [ ] API-Integration- und E2E-Tests (Cypress / Playwright)
- [ ] Frontend Unit-Test-Coverage ausbauen
