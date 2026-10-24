import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RUNTIME_CONFIG } from '@klarahome/util';

import { CanvasFrame, StorefrontCanvas } from './storefront-canvas';
import { SAND_TOKENS } from './storefront-theme';

@Component({
  imports: [StorefrontCanvas],
  template: `
    <kh-storefront-canvas
      [frames]="frames()"
      [preset]="preset()"
      [editable]="editable()"
      [selectedId]="selectedId()"
      (blockSelected)="selected.push($event)"
      (insertRequested)="inserted.push($event)"
      (blockKey)="keys.push($event.key)"
    />
  `,
})
class Host {
  readonly frames = signal<readonly CanvasFrame[]>([
    {
      id: 'a',
      label: 'Hero',
      view: {
        kind: 'richText',
        id: 'a',
        heading: 'Hello there',
        html: '<p>Body</p>',
        width: 'narrow',
      },
    },
    { id: 'b', label: 'Product carousel', view: null, note: 'Drawing this block…' },
  ]);
  readonly preset = signal<string | null>('coral');
  readonly editable = signal(true);
  readonly selectedId = signal<string | null>('a');
  readonly selected: string[] = [];
  readonly inserted: number[] = [];
  readonly keys: string[] = [];
}

/**
 * The canvas is scoped: the palette goes on its own wrapper and on nothing else.
 *
 * The storefront's header, blocks and footer draw inside it, so if the preset were applied to
 * `<html>` (or the wrapper's tokens leaked there) the back office itself would change colour.
 */
describe('StorefrontCanvas', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  let fixture: ComponentFixture<Host>;
  const stage = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-storefront-canvas]')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        provideRouter([]),
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it("sets the preset's tokens inline on its own stage", () => {
    expect(stage().style.getPropertyValue('--kh-theme')).toBe('coral');
    expect(stage().style.getPropertyValue('--color-primary')).toBe('#c8441f');
    // The storefront's shape and display face, not the admin's.
    expect(stage().style.getPropertyValue('--font-display')).toContain('Fraunces');
    expect(stage().style.getPropertyValue('--radius-lg')).toBe(SAND_TOKENS['--radius-lg']);
  });

  it('does not touch the admin document or the elements around the canvas', () => {
    const root = document.documentElement.style;
    expect(root.getPropertyValue('--color-primary')).toBe('');
    expect(root.getPropertyValue('--kh-theme')).toBe('');
    expect(document.body.style.getPropertyValue('--color-primary')).toBe('');
    expect((fixture.nativeElement as HTMLElement).style.getPropertyValue('--color-primary')).toBe('');
    expect(stage().parentElement?.style.getPropertyValue('--color-primary') ?? '').toBe('');
  });

  it('re-colours the stage when the preset changes, and falls back to sand without one', async () => {
    fixture.componentInstance.preset.set('emerald');
    fixture.detectChanges();
    expect(stage().style.getPropertyValue('--kh-theme')).toBe('emerald');
    expect(stage().style.getPropertyValue('--color-primary')).toBe('#047857');

    fixture.componentInstance.preset.set(null);
    fixture.detectChanges();
    expect(stage().style.getPropertyValue('--kh-theme')).toBe('sand');
    expect(stage().style.getPropertyValue('--color-primary')).toBe(SAND_TOKENS['--color-primary']);
  });

  it('draws the shared header and footer around the blocks', () => {
    expect(stage().querySelector('kh-site-header')).not.toBeNull();
    expect(stage().querySelector('kh-site-footer')).not.toBeNull();
    expect(stage().textContent).toContain('Hello there');
  });

  it('wraps each block in a labelled frame with a + in every gap when editable', () => {
    const frames = Array.from(stage().querySelectorAll<HTMLElement>('[data-block-id]'));
    expect(frames.map((frame) => frame.getAttribute('data-block-id'))).toEqual(['a', 'b']);
    expect(frames[0].classList.contains('selected')).toBe(true);
    expect(frames[1].classList.contains('selected')).toBe(false);
    expect(frames[0].textContent).toContain('Hero');
    // A block with no render yet says why instead of being a blank.
    expect(frames[1].textContent).toContain('Drawing this block');

    const gaps = Array.from(stage().querySelectorAll<HTMLElement>('[data-insert-at]'));
    expect(gaps.map((gap) => gap.getAttribute('data-insert-at'))).toEqual(['0', '1', '2']);
  });

  it('draws the plain page, with no frames, when it is not editable', () => {
    fixture.componentInstance.editable.set(false);
    fixture.detectChanges();

    expect(stage().querySelector('[data-block-id]')).toBeNull();
    expect(stage().querySelector('[data-insert-at]')).toBeNull();
    expect(stage().textContent).toContain('Hello there');
  });

  it('keeps the header and footer out of the tab order and the pointer', () => {
    const inert = Array.from(stage().querySelectorAll('.chrome'));
    expect(inert).toHaveLength(2);
    for (const element of inert) expect(element.hasAttribute('inert')).toBe(true);
  });
});
