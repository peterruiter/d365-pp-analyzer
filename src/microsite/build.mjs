/**
 * Builds the public site.
 *
 *   node src/microsite/build.mjs src/microsite src/PowerPete.Analyzer.Api/wwwroot
 *
 * English at the root and every other language in a folder of its own, because the root is
 * what a link in an email lands on and a language nobody chose is the one they read.
 *
 * Everything countable comes from build/contracts rather than from the content file: how many
 * rules there are, how many component types, which extraction modes exist and what each one
 * reaches. A marketing page that keeps its own copy of those numbers is a page that eventually
 * promises a client something the product does not do, and the first person to notice is the
 * client.
 */

import { readFileSync, writeFileSync, mkdirSync, copyFileSync, readdirSync, existsSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(process.argv[2] ?? here);
const out = resolve(process.argv[3] ?? join(here, '../PowerPete.Analyzer.Api/wwwroot'));
const contracts = resolve(join(source, '../../build/contracts'));

const read = (path) => JSON.parse(readFileSync(path, 'utf8'));

const content = read(join(source, 'content.json'));
const locales = read(join(contracts, 'locales.json'));
const rules = read(join(contracts, 'rule-catalogue.json'));
const components = read(join(contracts, 'component-model.json'));
const sources = read(join(contracts, 'extraction-sources.json'));

const escape = (value) => String(value)
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;');

/** The figures the page quotes, counted from the contracts every build. */
const counts = {
  rules: rules.rules.length,
  componentTypes: components.componentTypes.length,
  domains: new Set(components.componentTypes.map((type) => type.domain)).size,
  languages: locales.locales.length
};

const modes = sources.modes.map((mode) => ({
  id: mode.id,
  name: mode.name,
  summary: mode.summary,
  reaches: mode.reaches
}));

const evidence = Object.keys(modes[0]?.reaches ?? {});

function page(code, text) {
  const root = code === locales.default;
  const prefix = root ? '' : '../';
  const others = locales.locales.filter((locale) => locale.code !== code);

  const switcher = others.map((locale) => {
    const href = locale.code === locales.default ? (root ? '.' : '../') : `${root ? '' : '../'}${locale.code}/`;
    return `<a hreflang="${locale.code}" href="${href}">${escape(locale.nativeName)}</a>`;
  }).join('');

  const alternates = locales.locales.map((locale) => {
    const href = locale.code === locales.default ? '/' : `/${locale.code}/`;
    return `  <link rel="alternate" hreflang="${locale.code}" href="${href}">`;
  }).join('\n');

  const reachCell = (level) => {
    const label = text.reachLegend[level] ?? level;
    return `<td><span class="reach reach-${escape(level)}">${escape(label)}</span></td>`;
  };

  const modeRows = modes.map((mode) => `
          <tr>
            <th scope="row">
              <strong>${escape(mode.name)}</strong>
              <span>${escape(mode.summary)}</span>
            </th>
            ${evidence.map((key) => reachCell(mode.reaches[key] ?? 'none')).join('\n            ')}
          </tr>`).join('');

  const cards = (items) => items.map((item) => `
          <article class="card">
            <h3>${escape(item.head)}</h3>
            <p>${escape(item.body)}</p>
          </article>`).join('');

  const suite = text.suite.map((item) => `
          <article class="card${item.current ? ' card-current' : ''}">
            <h3>${escape(item.name)}</h3>
            <p>${escape(item.body)}</p>
          </article>`).join('');

  return `<!doctype html>
<html lang="${escape(text.lang)}" dir="${escape(text.dir)}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escape(text.title)}</title>
  <meta name="description" content="${escape(text.tagline)}">
  <link rel="icon" href="${prefix}favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="${prefix}site.css">
${alternates}
</head>
<body>
  <header class="top">
    <a class="wordmark" href="${root ? '.' : '../'}">
      <img src="${prefix}capgemini-white.svg" alt="Capgemini" height="26">
    </a>
    <nav class="languages" aria-label="Language">${switcher}</nav>
  </header>

  <main>
    <section class="hero">
      <h1>${escape(text.title)}</h1>
      <p class="tagline">${escape(text.tagline)}</p>
      <p class="intro">${escape(text.intro)}</p>
      <p class="actions">
        <a class="button" href="/app/">${escape(text.ctaPrimary)}</a>
        <a class="button button-quiet" href="/app/">${escape(text.ctaSecondary)}</a>
      </p>
    </section>

    <section class="band">
      <h2>${escape(text.countsTitle)}</h2>
      <div class="counts">
        <div><strong>${counts.rules}</strong><span>${escape(text.counts.rules)}</span></div>
        <div><strong>${counts.componentTypes}</strong><span>${escape(text.counts.componentTypes)}</span></div>
        <div><strong>${counts.domains}</strong><span>${escape(text.counts.domains)}</span></div>
        <div><strong>${counts.languages}</strong><span>${escape(text.counts.languages)}</span></div>
      </div>
    </section>

    <section>
      <h2>${escape(text.readsTitle)}</h2>
      <p class="lede">${escape(text.readsLede)}</p>

      <div class="table-wrap">
        <table class="reach-table">
          <thead>
            <tr>
              <th scope="col"></th>
              ${evidence.map((key) => `<th scope="col">${escape(text.reachColumns[key] ?? key)}</th>`).join('\n              ')}
            </tr>
          </thead>
          <tbody>${modeRows}
          </tbody>
        </table>
      </div>
    </section>

    <section>
      <h2>${escape(text.neverTitle)}</h2>
      <div class="cards">${cards(text.never)}
      </div>
    </section>

    <section>
      <h2>${escape(text.honestTitle)}</h2>
      <div class="cards cards-tight">${cards(text.honest)}
      </div>
    </section>

    <section>
      <h2>${escape(text.deliverTitle)}</h2>
      <div class="cards">${cards(text.deliver)}
      </div>
    </section>

    <section>
      <h2>${escape(text.suiteTitle)}</h2>
      <p class="lede">${escape(text.suiteLede)}</p>
      <div class="cards">${suite}
      </div>
    </section>
  </main>

  <footer>
    <span>${escape(text.footerRights)}</span>
    <span>${escape(text.footerBuilt)}</span>
  </footer>
</body>
</html>
`;
}

const missing = locales.locales.filter((locale) => !content[locale.code]);

if (missing.length > 0) {
  // Loud rather than quiet. A language in the picker with no page behind it is a link to a
  // 404, and the whole point of the site is that it does not promise what is not there.
  console.error(`The site has no content for: ${missing.map((locale) => locale.code).join(', ')}`);
  process.exit(1);
}

mkdirSync(out, { recursive: true });

for (const locale of locales.locales) {
  const text = content[locale.code];
  const directory = locale.code === locales.default ? out : join(out, locale.code);

  mkdirSync(directory, { recursive: true });
  writeFileSync(join(directory, 'index.html'), page(locale.code, text), 'utf8');
}

writeFileSync(join(out, 'site.css'), readFileSync(join(source, 'site.css'), 'utf8'), 'utf8');

// The marks the product uses, so the site and the product cannot show different ones.
const assets = resolve(join(source, '../web/public'));

if (existsSync(assets)) {
  for (const file of readdirSync(assets)) {
    copyFileSync(join(assets, file), join(out, file));
  }
}

console.log(
  `Microsite: ${locales.locales.length} languages, ` +
  `${counts.rules} rules, ${counts.componentTypes} component types, ${modes.length} extraction modes.`);
