#!/usr/bin/env node
/**
 * Klara Home — OpenAPI → Angular client generator.
 *
 * Reads the OpenAPI 3.1 document exported from the API host and writes the typed Angular client
 * into `src/frontend/libs/data-access/api/src/generated/`. The document is the contract
 * (docs/04-api-specification.md §7): there are no hand-written frontend DTOs, and CI fails if
 * the committed output differs from what this script produces.
 *
 * Why a generator we own rather than one off the shelf:
 *   - openapi-generator needs a JVM, which the frontend toolchain does not otherwise have;
 *   - the ones that do not need a JVM each impose their own HTTP abstraction, and this project
 *     has a specific one — an interceptor chain and a `HttpContext`-driven retry/loading policy
 *     that the generated code has to opt into per operation.
 *   - the input is not arbitrary OpenAPI. It is what ASP.NET Core 10 emits, which is a narrow,
 *     stable subset: `$ref`, objects with `required`, arrays, string enums, `type: ["null", T]`
 *     nullability, and `oneOf: [null, $ref]`. Everything else throws rather than guesses.
 *
 * Usage:
 *   node tools/generate-api-client.mjs [--document <path>] [--out <dir>] [--check]
 *
 *   --check  writes nothing; exits 1 if the output would differ from what is on disk.
 */

import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, mkdirSync, rmSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const DEFAULT_DOCUMENT = join(REPO_ROOT, 'artifacts', 'openapi', 'KlaraHome.Api.json');
const DEFAULT_OUT = join(REPO_ROOT, 'src', 'frontend', 'libs', 'data-access', 'api', 'src', 'generated');

const BANNER = `/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
`;

// ---------------------------------------------------------------------------------------------
// Arguments
// ---------------------------------------------------------------------------------------------

function parseArgs(argv) {
  const args = { document: DEFAULT_DOCUMENT, out: DEFAULT_OUT, check: false };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--check') args.check = true;
    else if (arg === '--document') args.document = resolve(argv[++i]);
    else if (arg === '--out') args.out = resolve(argv[++i]);
    else throw new Error(`Unknown argument: ${arg}`);
  }
  return args;
}

// ---------------------------------------------------------------------------------------------
// Naming
// ---------------------------------------------------------------------------------------------

const pascal = (value) => value.replace(/(^|[^a-zA-Z0-9])([a-zA-Z0-9])/g, (_, __, c) => c.toUpperCase());
const camel = (value) => {
  const p = pascal(value);
  return p.charAt(0).toLowerCase() + p.slice(1);
};
const kebab = (value) =>
  value
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/[^a-zA-Z0-9]+/g, '-')
    .toLowerCase();

/** A TypeScript identifier that never needs quoting, or a quoted key. */
const propertyKey = (name) => (/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) ? name : JSON.stringify(name));

/** Wraps a doc comment, or returns '' when there is nothing to say. */
function docComment(indent, ...lines) {
  const text = lines
    .filter(Boolean)
    .flatMap((l) => String(l).replace(/\*\//g, '*​/').split('\n'))
    .map((l) => l.trim())
    .filter(Boolean);
  if (text.length === 0) return '';
  if (text.length === 1) return `${indent}/** ${text[0]} */\n`;
  return `${indent}/**\n${text.map((l) => `${indent} * ${l}`).join('\n')}\n${indent} */\n`;
}

// ---------------------------------------------------------------------------------------------
// Schema → TypeScript
// ---------------------------------------------------------------------------------------------

/**
 * The handful of schema names ASP.NET Core emits for things that are not data shapes.
 * Mapping them here keeps the rest of the walker free of special cases.
 */
const INTRINSICS = new Map([
  ['IFormFile', 'Blob'],
  ['JsonElement', 'unknown'],
]);

const refName = (ref) => {
  const match = /^#\/components\/schemas\/(.+)$/.exec(ref);
  if (!match) throw new Error(`Only local component refs are supported, got: ${ref}`);
  return match[1];
};

/** Renders a schema as a TypeScript type expression. */
function typeOf(schema, ctx) {
  if (!schema || Object.keys(schema).length === 0) return 'unknown';

  if (schema.$ref) {
    const name = refName(schema.$ref);
    if (INTRINSICS.has(name)) return INTRINSICS.get(name);
    ctx.used.add(name);
    // Qualified here rather than by a pass over the finished text: a model called `UserType` and
    // a query parameter called `UserType` are the same word in two positions, and only the one
    // in type position is a model.
    return `${ctx.qualifier ?? ''}${name}`;
  }

  // `oneOf: [{ type: 'null' }, X]` — how the document spells a nullable reference.
  const union = schema.oneOf ?? schema.anyOf;
  if (union) {
    const parts = union.map((member) => (member.type === 'null' ? 'null' : typeOf(member, ctx)));
    return [...new Set(parts)].join(' | ');
  }

  // `type: ['null', T]` — how it spells a nullable primitive. `['integer', 'string']` is how it
  // spells a numeric query parameter that arrives as text; the client always sends the number.
  if (Array.isArray(schema.type)) {
    const nullable = schema.type.includes('null');
    const concrete = schema.type.filter((t) => t !== 'null');
    const preferred = concrete.includes('integer') || concrete.includes('number')
      ? concrete.find((t) => t === 'integer' || t === 'number')
      : concrete[0];
    const rendered = typeOf({ ...schema, type: preferred }, ctx);
    return nullable ? `${rendered} | null` : rendered;
  }

  if (schema.enum) {
    return schema.enum.map((value) => JSON.stringify(value)).join(' | ');
  }

  switch (schema.type) {
    case 'string':
      return 'string';
    case 'integer':
    case 'number':
      return 'number';
    case 'boolean':
      return 'boolean';
    case 'null':
      return 'null';
    case 'array':
      return `${maybeParenthesise(typeOf(schema.items ?? {}, ctx))}[]`;
    case 'object':
    case undefined: {
      if (schema.additionalProperties && schema.additionalProperties !== true) {
        return `Record<string, ${typeOf(schema.additionalProperties, ctx)}>`;
      }
      if (schema.properties) return inlineObject(schema, ctx);
      return 'Record<string, unknown>';
    }
    default:
      throw new Error(`Unsupported schema type: ${JSON.stringify(schema.type)}`);
  }
}

const maybeParenthesise = (type) => (/[|&]/.test(type) ? `(${type})` : type);

function inlineObject(schema, ctx) {
  const required = new Set(schema.required ?? []);
  const members = Object.entries(schema.properties ?? {}).map(([name, property]) => {
    const optional = required.has(name) ? '' : '?';
    return `${propertyKey(name)}${optional}: ${typeOf(property, ctx)}`;
  });
  return members.length === 0 ? 'Record<string, never>' : `{ ${members.join('; ')} }`;
}

/** Renders one named component schema as an exported declaration. */
function declaration(name, schema, ctx) {
  const summary = docComment('', schema.description, schema.title);

  if (schema.enum) {
    const values = schema.enum.map((value) => `  | ${JSON.stringify(value)}`).join('\n');
    return `${summary}export type ${name} =\n${values};\n`;
  }

  if (schema.type === 'object' || schema.properties) {
    const required = new Set(schema.required ?? []);
    const lines = Object.entries(schema.properties ?? {}).map(([property, propertySchema]) => {
      const optional = required.has(property) ? '' : '?';
      const comment = docComment('  ', propertySchema.description);
      return `${comment}  ${propertyKey(property)}${optional}: ${typeOf(propertySchema, ctx)};`;
    });
    const index =
      schema.additionalProperties && schema.additionalProperties !== true
        ? [`  [key: string]: ${typeOf(schema.additionalProperties, ctx)};`]
        : [];
    const body = [...lines, ...index].join('\n');
    return `${summary}export interface ${name} {\n${body || '  [key: string]: never;'}\n}\n`;
  }

  return `${summary}export type ${name} = ${typeOf(schema, ctx)};\n`;
}

// ---------------------------------------------------------------------------------------------
// Operations
// ---------------------------------------------------------------------------------------------

const HTTP_METHODS = ['get', 'put', 'post', 'delete', 'patch', 'head', 'options'];

/** Everything a generated method needs, read once so the emitter stays declarative. */
function readOperations(document) {
  const operations = [];

  for (const [path, item] of Object.entries(document.paths)) {
    for (const method of HTTP_METHODS) {
      const operation = item[method];
      if (!operation) continue;
      if (!operation.operationId) {
        throw new Error(`${method.toUpperCase()} ${path} has no operationId; the client cannot name it.`);
      }

      const parameters = [...(item.parameters ?? []), ...(operation.parameters ?? [])];
      const pathParameters = parameters.filter((p) => p.in === 'path');
      const queryParameters = parameters.filter((p) => p.in === 'query');
      const unsupported = parameters.filter((p) => p.in !== 'path' && p.in !== 'query');
      if (unsupported.length > 0) {
        throw new Error(
          `${operation.operationId} declares ${unsupported.map((p) => p.in).join(', ')} parameters, ` +
            'which this generator does not emit. Headers belong to the interceptor chain.'
        );
      }

      const requestBody = operation.requestBody;
      const bodyContent = requestBody?.content ?? {};
      const bodyMediaType = bodyContent['application/json']
        ? 'application/json'
        : bodyContent['multipart/form-data']
          ? 'multipart/form-data'
          : undefined;
      if (requestBody && !bodyMediaType) {
        throw new Error(`${operation.operationId} has a request body in an unsupported media type.`);
      }

      // The success response the caller gets back. 204 and the bodiless 2xx answers are `void`.
      const successCode = Object.keys(operation.responses ?? {})
        .filter((code) => code.startsWith('2'))
        .sort()[0];
      const successSchema = successCode
        ? operation.responses[successCode]?.content?.['application/json']?.schema
        : undefined;

      operations.push({
        tag: operation.tags?.[0] ?? 'Default',
        operationId: operation.operationId,
        summary: operation.summary,
        description: operation.description,
        method,
        path,
        pathParameters,
        queryParameters,
        bodySchema: bodyMediaType ? bodyContent[bodyMediaType].schema : undefined,
        bodyRequired: Boolean(requestBody?.required),
        multipart: bodyMediaType === 'multipart/form-data',
        successSchema,
      });
    }
  }

  operations.sort((a, b) => a.operationId.localeCompare(b.operationId));
  return operations;
}

/**
 * A query object is emitted as its own interface so callers can name it, and so an added
 * parameter shows up in review as a changed interface rather than a changed call site.
 */
function queryInterface(operation, ctx) {
  if (operation.queryParameters.length === 0) return undefined;
  const name = `${pascal(operation.operationId)}Query`;
  const members = operation.queryParameters
    .map((parameter) => {
      const optional = parameter.required ? '' : '?';
      const comment = docComment('  ', parameter.description);
      return `${comment}  ${propertyKey(parameter.name)}${optional}: ${typeOf(parameter.schema ?? {}, ctx)};`;
    })
    .join('\n');
  const allOptional = operation.queryParameters.every((parameter) => !parameter.required);
  return {
    name,
    allOptional,
    text: `${docComment('', `Query string for \`${operation.operationId}\`.`)}export interface ${name} {\n${members}\n}\n`,
  };
}

/** The URL as a template literal, with every path segment encoded. */
function urlExpression(operation) {
  const url = operation.path.replace(/\{([^}]+)\}/g, (_, name) => `\${encodeURIComponent(String(${camel(name)}))}`);
  return `\`\${this.baseUrl}${url}\``;
}

function emitService(tag, operations, ctx) {
  const className = `${pascal(tag)}ApiClient`;
  const queries = [];
  const methods = [];

  for (const operation of operations) {
    const query = queryInterface(operation, ctx);
    if (query) {
      // Two operations whose ids differ only in punctuation would produce one interface name and
      // silently give the second one the first one's parameters. Refuse instead.
      if (ctx.declared.has(query.name)) throw new Error(`Duplicate generated interface ${query.name}.`);
      ctx.declared.add(query.name);
      queries.push(query.text);
    }

    const signature = [];
    for (const parameter of operation.pathParameters) {
      signature.push({ required: true, text: `${camel(parameter.name)}: ${typeOf(parameter.schema ?? {}, ctx)}` });
    }
    if (operation.bodySchema) {
      const bodyType = typeOf(operation.bodySchema, ctx);
      signature.push({
        required: operation.bodyRequired,
        text: `body${operation.bodyRequired ? '' : '?'}: ${bodyType}`,
      });
    }
    if (query) {
      signature.push({ required: !query.allOptional, text: `query${query.allOptional ? '?' : ''}: ${query.name}` });
    }

    // Required parameters first: a body that may be omitted must not sit before a query that
    // may not, or the emitted signature would not compile.
    signature.sort((a, b) => Number(b.required) - Number(a.required));
    signature.push({ required: false, text: 'options?: ApiRequestOptions' });

    const returnType = operation.successSchema ? typeOf(operation.successSchema, ctx) : 'void';
    const comment = docComment(
      '  ',
      operation.summary,
      operation.description,
      `\`${operation.method.toUpperCase()} ${operation.path}\``
    );

    const requestArgs = [
      `'${operation.method.toUpperCase()}'`,
      urlExpression(operation),
      operation.bodySchema ? (operation.multipart ? 'toFormData(body)' : 'body') : 'undefined',
      query ? 'query' : 'undefined',
      'options',
    ];

    methods.push(
      `${comment}  ${operation.operationId}(${signature.map((p) => p.text).join(', ')}): Observable<${returnType}> {\n` +
        `    return this.http.request<${returnType}>(${requestArgs.join(', ')});\n` +
        `  }\n`
    );
  }

  // `toFormData` is only imported where an operation actually uploads something; an unused
  // import is a lint error the generated file should not have to disable.
  const runtimeImports = ['ApiRequestOptions', 'ApiTransport'];
  if (operations.some((operation) => operation.multipart)) runtimeImports.push('toFormData');

  const header =
    `${BANNER}/* eslint-disable */\n\n` +
    `import { Injectable, inject } from '@angular/core';\n` +
    `import { Observable } from 'rxjs';\n\n` +
    `import { ${runtimeImports.join(', ')} } from '../../runtime';\n` +
    (ctx.used.size > 0 ? `import type * as Models from '../models';\n` : '') +
    `\n`;

  const body =
    queries.join('\n') +
    `\n` +
    docComment('', `\`${tag}\` endpoints, generated from the API's OpenAPI document.`) +
    `@Injectable({ providedIn: 'root' })\n` +
    `export class ${className} {\n` +
    `  private readonly http = inject(ApiTransport);\n` +
    `  private readonly baseUrl = this.http.baseUrl;\n\n` +
    methods.join('\n') +
    `}\n`;

  return { className, header, body };
}

// ---------------------------------------------------------------------------------------------
// Emit
// ---------------------------------------------------------------------------------------------

function generate(document) {
  const files = new Map();
  const schemas = document.components?.schemas ?? {};

  // ---- models.ts ----
  const modelCtx = { used: new Set() };
  const declarations = Object.keys(schemas)
    .filter((name) => !INTRINSICS.has(name))
    .sort()
    .map((name) => declaration(name, schemas[name], modelCtx))
    .join('\n');
  files.set('models.ts', `${BANNER}/* eslint-disable */\n\n${declarations}`);

  // ---- services ----
  const operations = readOperations(document);
  const byTag = new Map();
  for (const operation of operations) {
    if (!byTag.has(operation.tag)) byTag.set(operation.tag, []);
    byTag.get(operation.tag).push(operation);
  }

  const exports = [];
  for (const tag of [...byTag.keys()].sort()) {
    // Model names are written as `Models.X` through a namespace import, so a schema called
    // `Observable` or `Injectable` cannot collide with the framework symbols the file needs.
    const ctx = { used: new Set(), declared: new Set(), qualifier: 'Models.' };
    const { className, header, body } = emitService(tag, byTag.get(tag), ctx);

    const fileName = `services/${kebab(tag)}.api-client.ts`;
    files.set(fileName, `${header}${body}`);
    exports.push({ className, fileName: fileName.replace(/\.ts$/, '') });
  }

  // ---- index.ts ----
  const lines = [
    `${BANNER}/* eslint-disable */`,
    '',
    // Imported as well as re-exported: `export *` makes a name visible to importers of this
    // module, not inside the module itself, and GENERATED_API_CLIENTS below is a list of values.
    ...exports.map((entry) => `import { ${entry.className} } from './${entry.fileName}';`),
    '',
    `export * from './models';`,
    ...exports.map((entry) => `export * from './${entry.fileName}';`),
    '',
    docComment(
      '',
      'Every generated client, so an app can assert at build time that it has them all.',
      'Consumers inject the individual clients; this exists for the DI smoke check.'
    ).trimEnd(),
    'export const GENERATED_API_CLIENTS = [',
    ...exports.map((entry) => `  ${entry.className},`),
    '] as const;',
    '',
  ];
  files.set('index.ts', lines.join('\n'));

  return files;
}

// ---------------------------------------------------------------------------------------------
// Disk
// ---------------------------------------------------------------------------------------------

function listFiles(dir, prefix = '') {
  if (!existsSync(dir)) return new Map();
  const found = new Map();
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    const key = prefix ? `${prefix}/${entry}` : entry;
    if (statSync(full).isDirectory()) for (const [k, v] of listFiles(full, key)) found.set(k, v);
    else found.set(key, readFileSync(full, 'utf8'));
  }
  return found;
}

// `\r\n` on Windows and `\n` in CI would make every file look changed, so the comparison and the
// write both use `\n`. .gitattributes already keeps the checkout consistent.
const normalise = (text) => text.replace(/\r\n/g, '\n');

function main() {
  const args = parseArgs(process.argv.slice(2));

  if (!existsSync(args.document)) {
    console.error(`OpenAPI document not found: ${args.document}`);
    console.error('Export it first:  pwsh tools/generate-api-client.ps1');
    process.exit(2);
  }

  const document = JSON.parse(readFileSync(args.document, 'utf8'));
  const generated = generate(document);

  const existing = listFiles(args.out);
  const changed = [];
  for (const [name, text] of generated) {
    if (normalise(existing.get(name) ?? '') !== normalise(text)) changed.push(name);
  }
  const removed = [...existing.keys()].filter((name) => !generated.has(name));

  if (args.check) {
    if (changed.length === 0 && removed.length === 0) {
      console.log(`API client is up to date (${generated.size} files).`);
      return;
    }
    console.error('The committed API client does not match the current OpenAPI document.');
    for (const name of changed) console.error(`  changed: ${name}`);
    for (const name of removed) console.error(`  removed: ${name}`);
    console.error('\nRegenerate it with:  pwsh tools/generate-api-client.ps1');
    process.exit(1);
  }

  rmSync(args.out, { recursive: true, force: true });
  for (const [name, text] of generated) {
    const target = join(args.out, name);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, normalise(text), 'utf8');
  }

  const hash = createHash('sha256').update(readFileSync(args.document)).digest('hex').slice(0, 12);
  const operations = readOperations(document).length;
  const schemas = Object.keys(document.components?.schemas ?? {}).length;
  console.log(
    `Generated ${generated.size} files into ${relative(REPO_ROOT, args.out)} ` +
      `(${operations} operations, ${schemas} schemas, document ${hash}).`
  );
}

main();
