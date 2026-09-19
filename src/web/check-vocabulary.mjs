// ===========================================================================
// Two checks the TypeScript compiler cannot make.
//
// A className is a string. A translation key is a string. Both compile
// perfectly when they are wrong, and both fail in the only place that matters:
// in front of a consultant, on a client's engagement.
//
// This ran because the workspace was built with an invented set of class names
// while the stylesheet came from another product, so every screen compiled,
// every test passed, and every page rendered as unstyled markup.
// ===========================================================================

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const source = 'src';
const bundles = '../PowerPete.Analyzer.Domain/Localization/Resources';

const pages = readdirSync(source).filter((name) => name.endsWith('.tsx'));
const styles = readdirSync(source).filter((name) => name.endsWith('.css'));

const css = styles.map((name) => readFileSync(join(source, name), 'utf8')).join('\n');
const defined = new Set([...css.matchAll(/\.([A-Za-z][A-Za-z0-9_-]*)/g)].map((match) => match[1]));

const english = JSON.parse(readFileSync(join(bundles, 'ui.en.json'), 'utf8'));

const problems = [];

for (const page of pages) {
  const text = readFileSync(join(source, page), 'utf8');

  // Literal class names only. A name assembled from an expression is checked by
  // whichever lookup table builds it, not here.
  const literals = [...text.matchAll(/className="([^"{}]*)"/g)].map((match) => match[1]);
  const templates = [...text.matchAll(/className=\{`([^`]*)`\}/g)]
    .map((match) => match[1].replace(/\$\{[^}]*\}/g, ' '));

  for (const group of [...literals, ...templates]) {
    for (const name of group.split(/\s+/).filter(Boolean)) {
      // A trailing fragment such as `checklist-` is the literal half of a name the
      // page completes at run time. The completed names are checked by their table.
      if (name.endsWith('-')) continue;
      if (!defined.has(name)) problems.push(`${page}: no rule for .${name}`);
    }
  }

  // t('some.key'), but not t('some.prefix.' + value), whose halves are checked by
  // whatever produces the second one.
  const dynamic = new Set([...text.matchAll(/\bt\(\s*'([^']+)'\s*\+/g)].map((match) => match[1]));

  for (const [, key] of text.matchAll(/\bt\(\s*'([^']+)'/g)) {
    if (dynamic.has(key)) continue;
    if (!(key in english)) problems.push(`${page}: no English string for '${key}'`);
  }
}

// Every namespace, in every language the product offers.
//
// Each bundle is checked against the English of its own namespace rather than against the
// interface strings, because the product now writes documents as well as screens: the report
// and the finding text are what a client reads, and they are translated separately from the
// buttons.
//
// The picker is built from the locale contract, so a language declared there and never
// translated is one a reader can choose and be shown English in. That is exactly what
// happened once: six languages were on offer and one existed. The fallback made it invisible,
// which is the whole problem with a good fallback.
const locales = JSON.parse(readFileSync('../../build/contracts/locales.json', 'utf8'));

const namespaces = [...new Set(
  readdirSync(bundles)
    .filter((file) => file.endsWith('.en.json'))
    .map((file) => file.slice(0, -'.en.json'.length)))].sort();

for (const ns of namespaces) {
  const reference = JSON.parse(readFileSync(join(bundles, `${ns}.en.json`), 'utf8'));

  for (const locale of locales.locales) {
    if (locale.code === 'en') continue;

    const path = join(bundles, `${ns}.${locale.code}.json`);

    let bundle;
    try {
      bundle = JSON.parse(readFileSync(path, 'utf8'));
    } catch {
      problems.push(`${ns}.${locale.code}.json is missing, and ${locale.englishName} is offered in the language picker`);
      continue;
    }

    const missing = Object.keys(reference).filter((key) => !(key in bundle));
    const extra = Object.keys(bundle).filter((key) => !(key in reference));

    if (missing.length > 0) {
      problems.push(
        `${ns}.${locale.code}.json is missing ${missing.length} strings, so ${locale.englishName} readers see English for them` +
        ` (first: ${missing.slice(0, 3).join(', ')})`);
    }

    // A key nothing can fall back to. Usually a rename that reached one language.
    if (extra.length > 0) {
      problems.push(
        `${ns}.${locale.code}.json has ${extra.length} keys that are not in ${ns}.en.json` +
        ` (first: ${extra.slice(0, 3).join(', ')})`);
    }

    // A placeholder that gained, lost or changed between languages formats wrongly at run
    // time, and the reader sees {0} or {componentName} in the middle of a sentence. Both
    // shapes are checked: the interface numbers its arguments and the backlog names them.
    for (const key of Object.keys(reference)) {
      if (!(key in bundle)) continue;

      const shape = (text) =>
        [...String(text).matchAll(/\{[A-Za-z0-9]+\}/g)].map((match) => match[0]).sort().join('');

      if (shape(reference[key]) !== shape(bundle[key])) {
        problems.push(`${ns}.${locale.code}.json: '${key}' does not carry the same placeholders as English`);
      }
    }
  }
}

if (problems.length > 0) {
  console.error(`\n${problems.length} problem${problems.length === 1 ? '' : 's'}:\n`);
  for (const problem of problems) console.error(`  ${problem}`);
  console.error('');
  process.exit(1);
}

console.log(
  `vocabulary: ${pages.length} pages, ${defined.size} classes, ` +
  `${namespaces.length} namespaces, ${locales.locales.length} languages, no problems`);
