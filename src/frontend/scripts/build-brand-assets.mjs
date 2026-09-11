#!/usr/bin/env node
/**
 * Rasterises the hand-authored brand SVGs under apps/storefront/public/brand into the PNG/ICO
 * files browsers and OSes expect, using the `playwright` devDependency already in the workspace
 * (for `admin-e2e`) — there is no `sharp`/ImageMagick available in this environment, and this
 * script is the documented, re-runnable substitute Step 30's brand-assets deliverable asks for
 * when no such tool exists.
 *
 * Usage: node scripts/build-brand-assets.mjs
 * Re-run this after editing any source SVG under apps/storefront/public/brand/.
 */
import { chromium } from 'playwright';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, '..');
const brandDir = join(root, 'apps/storefront/public/brand');
const adminBrandDir = join(root, 'apps/admin/public/brand');

async function renderPng(page, svgPath, size) {
  const svg = readFileSync(svgPath, 'utf8');
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent;}svg{display:block;width:${size}px;height:${size}px;}</style></head><body>${svg}</body></html>`,
  );
  const el = await page.$('svg');
  return el.screenshot({ omitBackground: true });
}

/** Builds a minimal ICO container embedding PNG-compressed images (supported since Windows Vista). */
function buildIco(pngBuffers) {
  const count = pngBuffers.length;
  const headerSize = 6;
  const dirEntrySize = 16;
  const dirSize = dirEntrySize * count;
  let offset = headerSize + dirSize;

  const header = Buffer.alloc(headerSize);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // type: icon
  header.writeUInt16LE(count, 4);

  const dirEntries = [];
  const imageBuffers = [];
  for (const { size, buffer } of pngBuffers) {
    const entry = Buffer.alloc(dirEntrySize);
    entry.writeUInt8(size >= 256 ? 0 : size, 0); // width, 0 means 256
    entry.writeUInt8(size >= 256 ? 0 : size, 1); // height
    entry.writeUInt8(0, 2); // color palette
    entry.writeUInt8(0, 3); // reserved
    entry.writeUInt16LE(1, 4); // color planes
    entry.writeUInt16LE(32, 6); // bits per pixel
    entry.writeUInt32LE(buffer.length, 8); // image data size
    entry.writeUInt32LE(offset, 12); // offset
    offset += buffer.length;
    dirEntries.push(entry);
    imageBuffers.push(buffer);
  }

  return Buffer.concat([header, ...dirEntries, ...imageBuffers]);
}

async function main() {
  const browser = await chromium.launch();
  const page = await browser.newPage();

  // Favicon PNGs (16/32/48) + ICO, from favicon.svg.
  const faviconSvg = join(brandDir, 'favicon.svg');
  const faviconSizes = [16, 32, 48];
  const faviconPngs = [];
  for (const size of faviconSizes) {
    const buf = await renderPng(page, faviconSvg, size);
    faviconPngs.push({ size, buffer: buf });
    writeFileSync(join(brandDir, `favicon-${size}.png`), buf);
  }
  const ico = buildIco(faviconPngs);
  writeFileSync(join(brandDir, 'favicon.ico'), ico);
  writeFileSync(join(root, 'apps/storefront/public/favicon.ico'), ico);

  // Same favicon for admin (admin has no distinct mark — one brand, two apps).
  mkdirSync(adminBrandDir, { recursive: true });
  writeFileSync(join(adminBrandDir, 'favicon.ico'), ico);
  writeFileSync(join(root, 'apps/admin/public/favicon.ico'), ico);

  // PWA icons (storefront only — admin has no PWA/manifest configuration to hang these off).
  for (const size of [192, 512]) {
    const buf = await renderPng(page, join(brandDir, 'icon.svg'), size);
    writeFileSync(join(brandDir, `icon-${size}.png`), buf);
  }
  for (const size of [192, 512]) {
    const buf = await renderPng(page, join(brandDir, 'icon-maskable.svg'), size);
    writeFileSync(join(brandDir, `icon-maskable-${size}.png`), buf);
  }

  // Email header mark, 96x96 (docs/steps/step-30 email/PDF template styling deliverable):
  // transactional emails cannot load an external stylesheet or a self-hosted SVG, so the header
  // glyph is embedded as a CID-linked PNG instead. Square, transparent, same source as every
  // other rasterisation — no new artwork.
  const markBuf = await renderPng(page, join(brandDir, 'mark.svg'), 96);
  writeFileSync(join(brandDir, 'mark-email.png'), markBuf);

  // OG image, 1200x630 (renderPng assumes a square viewport, so set it directly here instead).
  await page.setViewportSize({ width: 1200, height: 630 });
  const ogSvg = readFileSync(join(brandDir, 'og-default.svg'), 'utf8');
  await page.setContent(
    `<!doctype html><html><head><style>html,body{margin:0;padding:0;}svg{display:block;width:1200px;height:630px;}</style></head><body>${ogSvg}</body></html>`,
  );
  const ogEl = await page.$('svg');
  const ogBuf = await ogEl.screenshot();
  writeFileSync(join(brandDir, 'og-default.png'), ogBuf);

  await browser.close();
  console.log('Brand assets rasterised into', brandDir, 'and', adminBrandDir);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
