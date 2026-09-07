import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const serverDistFolder = dirname(fileURLToPath(import.meta.url));
const browserDistFolder = resolve(serverDistFolder, '../browser');

const app = express();

/**
 * The hostnames this process will answer for.
 *
 * Angular's server engine refuses a request whose `Host` header it does not recognise, and with an
 * empty list that is **every** request — the SSR process answers 400 to everything, which is a
 * failure that only appears once the container is behind a real hostname. It is a deliberate
 * protection (a forged `Host` is how a server-side renderer is turned into an open redirect or an
 * SSRF hop), so the answer is to configure it rather than to switch it off.
 *
 * `KH_ALLOWED_HOSTS` is a comma-separated list and takes wildcards — `*.klarahome.example`. The
 * default covers a developer running the built server on their own machine and nothing else, so a
 * deployment that forgets to set it fails loudly at its own hostname rather than quietly trusting
 * one it was never given.
 */
const allowedHosts = (process.env['KH_ALLOWED_HOSTS'] ?? 'localhost,127.0.0.1,[::1]')
  .split(',')
  .map((host) => host.trim())
  .filter((host) => host.length > 0);

/**
 * Whether `X-Forwarded-*` may be believed.
 *
 * True in this platform's deployments, where Traefik terminates TLS and is the only thing that can
 * reach this process (docs/06-infrastructure-devops.md). It must stay false anywhere the process
 * is directly reachable, because a header an end user can set is not evidence of anything.
 */
const trustProxyHeaders = process.env['KH_TRUST_PROXY_HEADERS'] === 'true';

const angularApp = new AngularNodeAppEngine({ allowedHosts, trustProxyHeaders });

/**
 * Where the API is, for the three documents this process fetches for itself.
 *
 * The same variable the Angular application reads through `runtimeConfigFromEnv`; read here
 * directly so this file has no dependency on the browser bundle.
 */
const apiBaseUrl = (process.env['KH_API_BASE_URL'] ?? 'https://api.klarahome.localhost').replace(/\/+$/, '');

/**
 * Five minutes on the SEO documents: long enough that a crawl does not hammer the API, short
 * enough that switching indexing off takes effect while somebody is still watching it.
 */
const SEO_CACHE_CONTROL = 'public, max-age=300';

/**
 * `robots.txt` and the sitemaps, served from the shop's own origin.
 *
 * A crawler looks for both at the site root and nowhere else — `klarahome.example/robots.txt`,
 * not the API's hostname — so the storefront has to publish them even though the CMS decides
 * their contents (docs/05-frontend-architecture.md §3.5). The API answers JSON; the XML is
 * assembled here, which keeps sitemap formatting out of a module that has no business knowing
 * about it.
 *
 * All three are declared before the static middleware and before Angular, so neither can shadow
 * them.
 */
app.get('/robots.txt', async (_request, response) => {
  try {
    const upstream = await fetch(`${apiBaseUrl}/api/v1/store/content/seo/robots`, {
      headers: { accept: 'text/plain' },
    });
    if (!upstream.ok) throw new Error(`robots upstream answered ${upstream.status}`);

    response
      .status(200)
      .type('text/plain')
      .set('Cache-Control', SEO_CACHE_CONTROL)
      .send(await upstream.text());
  } catch {
    // A robots file that cannot be produced must not become a robots file that allows everything:
    // an unreachable CMS is not a decision to be indexed.
    response.status(200).type('text/plain').send(['User-agent: *', 'Disallow: /', ''].join('\n'));
  }
});

/** The sitemap index: one entry per section page, as the API enumerates them. */
app.get('/sitemap.xml', async (_request, response) => {
  try {
    const index = await fetchJson<{ entries?: SitemapIndexEntry[] }>(
      `${apiBaseUrl}/api/v1/store/content/seo/sitemap`,
    );

    const body = xmlDocument(
      'sitemapindex',
      (index.entries ?? []).map((entry) =>
        xmlElement('sitemap', [locElement(entry.loc), lastModElement(entry.lastModified)]),
      ),
    );

    response.status(200).type('application/xml').set('Cache-Control', SEO_CACHE_CONTROL).send(body);
  } catch {
    response.status(503).type('text/plain').send('Sitemap unavailable.');
  }
});

/**
 * One page of one section — the URLs the index points at.
 *
 * The path shape is the API's: `SitemapBuilder` writes its index entries as
 * `/sitemap/{section}/{page}.xml`, so this route matches that rather than something of its own
 * invention. The extension is stripped in code rather than expressed in the route pattern,
 * because Express 5 matches a literal after a parameter differently from Express 4 — and a route
 * that silently stops matching is a sitemap that silently disappears.
 */
app.get('/sitemap/:section/:page', async (request, response) => {
  const { section, page } = request.params as { section: string; page: string };
  const pageNumber = Number.parseInt(page.replace(/\.xml$/, ''), 10);

  // Validated rather than passed through: both values are interpolated into an upstream URL.
  if (!/^[a-z-]{1,32}$/.test(section) || !Number.isInteger(pageNumber) || pageNumber < 1) {
    response.status(404).type('text/plain').send('Not found.');
    return;
  }

  try {
    const document = await fetchJson<{ urls?: SitemapUrl[] }>(
      `${apiBaseUrl}/api/v1/store/content/seo/sitemap/${encodeURIComponent(section)}?page=${pageNumber}`,
    );

    const body = xmlDocument(
      'urlset',
      (document.urls ?? []).map((url) =>
        xmlElement('url', [
          locElement(url.loc),
          lastModElement(url.lastModified),
          `<changefreq>${escapeXml(url.changeFrequency)}</changefreq>`,
          `<priority>${url.priority.toFixed(1)}</priority>`,
        ]),
      ),
    );

    response.status(200).type('application/xml').set('Cache-Control', SEO_CACHE_CONTROL).send(body);
  } catch {
    response.status(503).type('text/plain').send('Sitemap unavailable.');
  }
});

/**
 * The built browser assets.
 *
 * `maxAge: '1y'` is safe because every file that can change carries a content hash in its name —
 * `outputHashing: 'all'` in the build configuration. `index: false` so a request for `/` reaches
 * Angular and is rendered, rather than being served as a static shell with no content in it.
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Everything else is the Angular application.
 *
 * Mounted with no path: Express 5 uses `path-to-regexp` v8, in which Express 4's `'/**'` is no
 * longer a valid pattern. A middleware with no path matches every request, which is what that
 * pattern always meant here.
 */
app.use((request, response, next) => {
  angularApp
    .handle(request)
    .then((rendered) => (rendered ? writeResponseToNodeResponse(rendered, response) : next()))
    .catch(next);
});

interface SitemapIndexEntry {
  readonly loc: string;
  readonly lastModified: string | null;
}

interface SitemapUrl {
  readonly loc: string;
  readonly lastModified: string | null;
  readonly changeFrequency: string;
  readonly priority: number;
}

async function fetchJson<T>(url: string): Promise<T> {
  const upstream = await fetch(url);
  if (!upstream.ok) throw new Error(`${url} answered ${upstream.status}`);
  return (await upstream.json()) as T;
}

/** Wraps a set of elements in the sitemap namespace and an XML declaration. */
function xmlDocument(root: string, children: readonly string[]): string {
  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    `<${root} xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">`,
    ...children,
    `</${root}>`,
    '',
  ].join('\n');
}

function xmlElement(name: string, children: readonly (string | null)[]): string {
  const body = children.filter((child): child is string => child !== null).join('');
  return `  <${name}>${body}</${name}>`;
}

const locElement = (loc: string): string => `<loc>${escapeXml(loc)}</loc>`;

/** Omitted rather than emitted empty when there is no date — `<lastmod></lastmod>` is invalid. */
const lastModElement = (value: string | null): string | null =>
  value ? `<lastmod>${escapeXml(value)}</lastmod>` : null;

/** Escapes the five characters that are not legal as text inside an XML element. */
function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

/**
 * Starts the server when this module is the entry point, or when it is run under PM2.
 * The port comes from `PORT`, defaulting to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, () => {
    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/** Request handler used by the Angular CLI during development and by the production container. */
export const reqHandler = createNodeRequestHandler(app);
