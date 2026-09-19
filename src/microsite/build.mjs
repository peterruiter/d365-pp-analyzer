// ===========================================================================
// Builds the public website, one copy per language.
//
// The same shape as the Intent Miner and Contact Center Migrator sites, because the three
// products sit behind the same wordmark and a visitor who has seen one should recognise the
// next. The stylesheet, the script and the chrome are ported from the migrator; what differs
// is this product's sections and the contracts they are built from.
//
// No dependencies, deliberately. It is string substitution over a handful of files, and a
// template engine would be a supply chain for a public page.
// ===========================================================================

import { readFileSync, writeFileSync, mkdirSync, existsSync, readdirSync, statSync, copyFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { createHash } from 'node:crypto';

const [, , sourceArg, outputArg, ...flags] = process.argv;

if (!sourceArg || !outputArg) {
  console.error('usage: node build.mjs <source> <output> [--skip-assets]');
  process.exit(2);
}

const source = resolve(sourceArg);
const output = resolve(outputArg);
const skipAssets = flags.includes('--skip-assets');

const templatePath = join(source, 'templates');
const contentPath = join(source, 'content');

// Nothing countable on the page is written here. The rule count, the component types, the
// extraction modes and what each one reaches are read from the contracts the product itself
// runs on, so the site cannot promise something the product does not do, and adding a rule
// updates the page without anybody remembering to.
const contractsPath = join(source, '..', '..', 'build', 'contracts');

// The languages, in the order the chooser lists them. English first because it is the
// fallback and the root of the site; the rest alphabetically by their own name.
const languages = [
  { code: 'en', native: 'English' },
  { code: 'de', native: 'Deutsch' },
  { code: 'es', native: 'Espa&ntilde;ol' },
  { code: 'fr', native: 'Fran&ccedil;ais' },
  { code: 'it', native: 'Italiano' },
  { code: 'nl', native: 'Nederlands' }
];

// The pages to build, and whether the header starts with a background. The home page opens
// on a full height hero and the bar fades in over it; every other page opens on a heading
// band, where a transparent bar would sit on nothing.
const pages = [
  { template: 'index.html', output: 'index.html', stuck: false },
  { template: 'privacy.html', output: 'privacy.html', stuck: true }
];

// The globe, drawn rather than fetched: an icon font for one glyph is a request and a
// dependency.
const globeIcon =
  '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true">' +
  '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18a14 14 0 0 1 0-18"/></svg>';

function readContent(code) {
  const file = join(contentPath, `${code}.json`);
  if (!existsSync(file)) throw new Error(`No text file for '${code}'. Expected ${file}.`);
  return JSON.parse(readFileSync(file, 'utf8'));
}

function readContract(name) {
  const file = join(contractsPath, name);
  if (!existsSync(file)) throw new Error(`Missing contract ${file}. It is the source of truth for this page.`);
  return JSON.parse(readFileSync(file, 'utf8'));
}

// English is the root of the site rather than /en/, because a visitor who types the bare
// domain should land somewhere rather than be redirected.
function pageUrl(code, page) {
  const prefix = code === 'en' ? '/' : `/${code}/`;
  return page === 'index.html' ? prefix : `${prefix}${page}`;
}

function copyTree(from, to) {
  mkdirSync(to, { recursive: true });
  for (const entry of readdirSync(from)) {
    const src = join(from, entry);
    const dst = join(to, entry);
    if (statSync(src).isDirectory()) copyTree(src, dst);
    else copyFileSync(src, dst);
  }
}

// The stylesheet and the script are shared by every page in every language, so they cannot
// be content hashed into their filenames without rewriting every reference. A hash in the
// query string does the same job: the URL changes when the bytes change, so a returning
// visitor is never served yesterday's stylesheet with today's markup.
function assetUrl(path) {
  const file = join(source, path);
  if (!existsSync(file)) return '/' + path;
  const hash = createHash('sha256').update(readFileSync(file)).digest('hex').slice(0, 10);
  return `/${path}?v=${hash}`;
}

const escape = (value) => String(value)
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;');

const components = readContract('component-model.json');
const rules = readContract('rule-catalogue.json');
const sources = readContract('extraction-sources.json');

const counts = {
  rules: rules.rules.length,
  componentTypes: components.componentTypes.length,
  domains: new Set(components.componentTypes.map((type) => type.domain)).size,
  languages: languages.length
};

/**
 * The ways into an estate, and honestly how much each one gives up.
 *
 * The reach is the point of this section rather than decoration. What a mode cannot reach
 * decides which rules can run at all, and a client deciding between a service principal and
 * a solution file is deciding how much of their estate gets read. Counted from the contract,
 * so a mode that loses an evidence source cannot keep claiming it here.
 */
function modesMarkup(text) {
  const tile = (mode) => {
    const name = text[`modes.name.${mode.id}`] ?? mode.name;
    const summary = text[`modes.summary.${mode.id}`] ?? mode.summary;

    const reach = Object.entries(mode.reaches ?? {}).map(([evidence, level]) => {
      const label = text[`modes.evidence.${evidence}`] ?? evidence;
      const value = text[`modes.reach.${level}`] ?? level;
      return `              <li><span class="reach-name">${escape(label)}</span>` +
             `<span class="reach reach--${escape(level)}">${escape(value)}</span></li>`;
    }).join('\n');

    return `          <li class="connector">
            <h3>${escape(name)}</h3>
            <p>${escape(summary)}</p>
            <ul class="reach-list">
${reach}
            </ul>
          </li>`;
  };

  return `        <ul class="connector-grid">
${sources.modes.map(tile).join('\n')}
        </ul>`;
}

/**
 * What it checks for, by category, counted from the catalogue.
 *
 * Categories rather than the thirty-seven rules themselves. A visitor wants to know whether
 * this looks at the thing they are worried about; the rule list belongs in the product.
 */
function categoriesMarkup(text) {
  const byCategory = new Map();

  for (const rule of rules.rules) {
    byCategory.set(rule.category, (byCategory.get(rule.category) ?? 0) + 1);
  }

  const tile = ([category, count]) => {
    const name = text[`rules.category.${category}`] ?? category;
    const body = text[`rules.categoryBody.${category}`] ?? '';
    const suffix = (text['rules.count'] ?? '{0} checks').replace('{0}', String(count));

    return `          <li class="connector">
            <h3>${escape(name)}</h3>
            <p>${escape(body)}</p>
            <p class="rule-count">${escape(suffix)}</p>
          </li>`;
  };

  const ordered = [...byCategory.entries()].sort((a, b) => b[1] - a[1]);

  return `        <ul class="connector-grid">
${ordered.map(tile).join('\n')}
        </ul>`;
}

const cssUrl = assetUrl('css/site.css');
const jsUrl = assetUrl('js/site.js');

const english = readContent('en');
let missing = 0;

for (const language of languages) {
  const code = language.code;
  const text = readContent(code);

  for (const page of pages) {
    const templateFile = join(templatePath, page.template);
    if (!existsSync(templateFile)) throw new Error(`Missing template ${templateFile}.`);

    let html = readFileSync(templateFile, 'utf8');

    // Partials first, so a placeholder inside one is substituted along with everything else.
    // A plain split and join rather than a regular expression replacement, because the
    // partial is HTML and $ sequences in a replacement string have their own meaning.
    let guard = 0;
    let match;
    while ((match = /\{\{>\s*([A-Za-z0-9_]+)\s*\}\}/.exec(html))) {
      if (++guard > 20) throw new Error(`Partials in ${page.template} nest too deeply, or include each other.`);
      const partialFile = join(templatePath, `${match[1]}.html`);
      if (!existsSync(partialFile)) throw new Error(`Missing partial ${partialFile}.`);
      html = html.split(match[0]).join(readFileSync(partialFile, 'utf8'));
    }

    // The chooser, built per page so every row links to that language's copy of the page
    // being read rather than to its home page: changing language should not lose your place.
    const options = languages.map((option) => {
      const current = option.code === code;
      const href = pageUrl(option.code, page.output);
      const tick = current ? '<span class="tick" aria-hidden="true">&#10003;</span>' : '';
      return `    <a class="lang-option" href="${href}" lang="${option.code}" hreflang="${option.code}" aria-current="${current}">` +
             `<span class="badge">${option.code}</span><span>${option.native}</span>${tick}</a>`;
    }).join('\n');

    const alternates = languages
      .map((option) => `<link rel="alternate" hreflang="${option.code}" href="${pageUrl(option.code, page.output)}">`)
      .concat(`<link rel="alternate" hreflang="x-default" href="${pageUrl('en', page.output)}">`)
      .join('\n');

    const themeToggle =
      `<button class="theme-toggle" type="button" role="switch" aria-checked="false" ` +
      `data-label-light="${text['theme.lightOn']}" data-label-dark="${text['theme.darkOn']}" title="${text['theme.toggle']}">\n` +
      `      <span class="visually-hidden" data-theme-status>${text['theme.lightOn']}</span>\n` +
      `      <span class="icon sun" aria-hidden="true"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg></span>\n` +
      `      <span class="icon moon" aria-hidden="true"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg></span>\n` +
      `      <span class="ball" aria-hidden="true"></span>\n` +
      `    </button>`;

    const computed = {
      htmlLang: code,
      lang: code.toUpperCase(),
      stuck: page.stuck ? ' is-stuck' : '',
      'url.home': pageUrl(code, 'index.html'),
      'url.privacy': pageUrl(code, 'privacy.html'),
      alternates,
      'lang.options': options,
      'icon.globe': globeIcon,
      'chrome.themeToggle': themeToggle,
      'asset.css': cssUrl,
      'asset.js': jsUrl,
      modes: modesMarkup(text),
      categories: categoriesMarkup(text),
      'stat.rules': String(counts.rules),
      'stat.componentTypes': String(counts.componentTypes),
      'stat.domains': String(counts.domains),
      'stat.languages': String(counts.languages)
    };

    // Computed values win over the text file, so a translation cannot redefine a URL.
    html = html.replace(/\{\{([A-Za-z0-9_.]+)\}\}/g, (whole, key) => {
      if (Object.prototype.hasOwnProperty.call(computed, key)) return String(computed[key]);
      if (Object.prototype.hasOwnProperty.call(text, key)) return String(text[key]);
      if (Object.prototype.hasOwnProperty.call(english, key)) {
        missing++;
        console.warn(`    [warn] ${code}/${page.output}: '${key}' is not translated, using English.`);
        return String(english[key]);
      }
      throw new Error(`Placeholder {{${key}}} in ${page.template} has no value in any language. Check the template for a typo.`);
    });

    // Every local asset picks up a content hash on its way out, so a returning visitor is
    // never served yesterday's image with today's markup.
    html = html.replace(/(src|href)="\/(assets\/[^"?]+)"/g, (whole, attribute, path) =>
      `${attribute}="${assetUrl(path)}"`);

    const destination = code === 'en' ? output : join(output, code);
    mkdirSync(destination, { recursive: true });

    // No byte order mark. One at the top of an HTML file is served as content, and some
    // parsers put it on the page before the doctype.
    writeFileSync(join(destination, page.output), html, 'utf8');
  }

  console.log(`    [ok]   ${language.native.replace(/&[a-z]+;/g, '')} (${code})`);
}

if (!skipAssets) {
  // Only these three. The templates and the text files are source, and copying the whole of
  // src/microsite would publish them, which is how the words a page is built from end up
  // served next to the page.
  for (const folder of ['css', 'js', 'assets']) {
    const from = join(source, folder);
    if (existsSync(from)) copyTree(from, join(output, folder));
  }
  console.log('    [ok]   Stylesheet, script and images copied to the root.');
}

if (missing > 0) {
  console.warn(`    [warn] ${missing} string(s) fell back to English.`);
}

console.log(
  `    [ok]   Public site written to ${output} in ${languages.length} languages: ` +
  `${counts.rules} rules, ${counts.componentTypes} component types, ${sources.modes.length} extraction modes.`);
