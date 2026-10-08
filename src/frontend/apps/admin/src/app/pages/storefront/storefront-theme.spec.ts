/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { THEME_PRESETS } from '@klarahome/util';

import { SAND_TOKENS, canvasTokens } from './storefront-theme';

/**
 * The canvas must wear the storefront's tokens, whatever the admin's skin sets.
 *
 * Both halves of "scoped" are tested: that the wrapper's map is complete (every token the admin
 * re-maps on `:root` has a storefront value to put back), and that a preset only ever changes the
 * wrapper's map. The component test beside it checks the map reaches the wrapper and nothing else.
 */
describe('Storefront canvas tokens', () => {
  it('starts from sand and names it', () => {
    expect(canvasTokens(null)['--kh-theme']).toBe('sand');
    expect(canvasTokens('does-not-exist')).toBe(SAND_TOKENS);
    expect(canvasTokens('sand')).toBe(SAND_TOKENS);
  });

  it('lays a preset over the base, so what it omits is the storefront value and not the admin one', () => {
    for (const preset of THEME_PRESETS.filter((candidate) => candidate.id !== 'sand')) {
      const tokens = canvasTokens(preset.id);
      expect(tokens['--kh-theme']).toBe(preset.id);
      for (const [name, value] of Object.entries(preset.tokens)) expect(tokens[name]).toBe(value);
      // The shape and type the presets leave alone are still the storefront's.
      expect(tokens['--radius-lg']).toBe(SAND_TOKENS['--radius-lg']);
      expect(tokens['--font-display']).toContain('Fraunces');
    }
    expect(canvasTokens('coral')['--color-primary']).not.toBe(SAND_TOKENS['--color-primary']);
  });

  it('puts back every token the admin skin re-maps', () => {
    // Read from the stylesheet itself, so a token added to the admin skin later fails here until
    // the canvas has a storefront value for it.
    const css = readFileSync(resolve(process.cwd(), 'apps/admin/src/styles/_admin-theme.scss'), 'utf-8');
    const light = css.slice(css.indexOf(':root {'), css.indexOf(':root.dark'));
    const declared = [...light.matchAll(/^\s+(--[a-z0-9-]+):/gm)].map((match) => match[1]);
    const storefrontSide = declared.filter(
      (name) =>
        /^--(color|radius|shadow|font|weight|tracking|badge)-/.test(name) ||
        name === '--header-height' ||
        name === '--bottom-bar-height',
    );

    expect(storefrontSide.length).toBeGreaterThan(40);
    const missing = storefrontSide.filter((name) => !(name in SAND_TOKENS));
    expect(missing).toEqual([]);
  });
});
