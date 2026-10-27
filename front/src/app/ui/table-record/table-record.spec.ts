import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MiddleText, RecordRow, RecordToggle } from './table-record';

@Component({
  imports: [MiddleText, RecordRow, RecordToggle],
  template: `<table><tbody><tr appRecordRow><td><app-middle-text [value]="value" /></td><td><app-record-toggle /></td></tr><tr appRecordRow><td>Second</td><td><app-record-toggle /></td></tr></tbody></table>`,
})
class Records {
  value = 'START-very-long-record-identifier-END';
}

describe('Mobile table records', () => {
  it('preserves both ends and the complete accessible value; expands only its own record', async () => {
    const fixture = TestBed.createComponent(Records);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    const text = root.querySelector('app-middle-text')!;
    expect(text.getAttribute('title')).toBe(fixture.componentInstance.value);
    expect(text.querySelector('.start')!.textContent).toContain('START-');
    expect(text.querySelector('.end')!.textContent).toContain('-END');
    const buttons = root.querySelectorAll<HTMLButtonElement>('.record-toggle');
    buttons[0].click();
    await fixture.whenStable();
    expect(buttons[0].getAttribute('aria-expanded')).toBe('true');
    expect(buttons[1].getAttribute('aria-expanded')).toBe('false');
    buttons[0].click();
    await fixture.whenStable();
    expect(buttons[0].getAttribute('aria-expanded')).toBe('false');
  });
});
