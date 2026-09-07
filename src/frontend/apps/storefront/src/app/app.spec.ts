import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RUNTIME_CONFIG } from '@klarahome/util';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';

import { App } from './app';

/**
 * A smoke test for the shell, not for its behaviour.
 *
 * It stands the app up against the real provider chain — runtime config, locale, the HTTP stack
 * and its five interceptors — so that a provider which cannot be constructed fails here rather
 * than as a blank page in a browser. `provideHttpClientTesting` replaces only the backend, so the
 * interceptors still run and the shell's start-up reads simply never answer, which is exactly the
 * state a slow connection puts it in.
 *
 * The landmarks are asserted because they are the accessibility contract every later step builds
 * on: a `header`, a `main` to skip to, a `footer`, and a skip link that reaches the content.
 */
describe('App shell', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
        provideKlaraHomeHttp(config),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();
  });

  it('renders the main landmark and a skip link to it', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const main = element.querySelector('main');
    const skipLink = element.querySelector('a.kh-skip-link');

    expect(main).not.toBeNull();
    expect(main?.id).toBe('main-content');
    expect(skipLink?.getAttribute('href')).toBe('#main-content');
  });

  it('renders the header and footer around the content', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;

    // The store name falls back to its default while `/store/config` is unanswered, which is the
    // point: a header with no name in it is a header nobody can navigate from.
    expect(element.querySelector('header')?.textContent).toContain('Klara Home');
    expect(element.querySelector('footer')).not.toBeNull();
    expect(element.querySelector('form[role="search"]')).not.toBeNull();
  });
});
