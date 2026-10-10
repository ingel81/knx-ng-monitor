import { Component } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { KnxIconRegistry } from '../../core/services/knx-icon-registry.service';
import { DateTimeFieldComponent } from './date-time-field.component';

@Component({
  standalone: true,
  imports: [FormsModule, DateTimeFieldComponent],
  template: `<knx-date-time-field [(ngModel)]="value" defaultTime="23:59" [min]="min" />`,
})
class HostComponent {
  value = '';
  min = '';
}

describe('DateTimeFieldComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let inputs: HTMLInputElement[];

  const type = (input: HTMLInputElement, text: string) => {
    input.value = text;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  };

  beforeEach(fakeAsync(() => {
    TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideNoopAnimations()] });
    TestBed.inject(KnxIconRegistry).register();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    tick();
    inputs = Array.from(fixture.nativeElement.querySelectorAll('input'));
  }));

  const field = () => fixture.debugElement.children[0].componentInstance as DateTimeFieldComponent;

  it('shows a model value in the date and time inputs', fakeAsync(() => {
    host.value = '2026-09-24T07:05:30';
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    expect(inputs[0].value).toMatch(/2026/);
    expect(inputs[1].value).toBe('07:05');
  }));

  it('emits the local datetime-local shape and fills the default time', fakeAsync(() => {
    // The placeholder tells the field order of the running browser's locale.
    const order = inputs[0].placeholder;
    const text = /^(MM|M)/.test(order) ? '09/24/2026' : /^(YYYY|JJJJ)/.test(order) ? '2026-09-24' : '24.09.2026';
    type(inputs[0], text);
    tick();
    expect(host.value).toBe('2026-09-24T23:59');
    type(inputs[1], '14:30');
    tick();
    expect(host.value).toBe('2026-09-24T14:30');
  }));

  it('turns an invalid entry into an empty value and flags it (#41)', fakeAsync(() => {
    host.value = '2026-09-24T14:30';
    fixture.detectChanges();
    tick();
    type(inputs[0], '08.10.202600');
    tick();
    expect(host.value).toBe('');
    expect(field().showInvalid).toBeTrue();
    expect(inputs[0].getAttribute('aria-invalid')).toBe('true');
  }));

  it('flags a value before min', fakeAsync(() => {
    host.min = '2026-09-24T14:30';
    host.value = '2026-09-24T10:00';
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    expect(field().outOfOrder).toBeTrue();
  }));
});
