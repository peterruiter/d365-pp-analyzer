import { useEffect, useMemo, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useLanguage, useT } from './i18n';

/**
 * file, title key, summary key.
 *
 * Module scope, so these stay keys until a component renders them. A title translated at
 * module load would be translated once, in whatever language the first reader happened to
 * have, and stay that way for everybody after them.
 */
const guides = [
  ['10-what-this-is.md', 'docs.what-this-is', 'docs.what-this-is-summary'],
  ['20-getting-access.md', 'docs.getting-access', 'docs.getting-access-summary'],
  ['30-connecting-an-environment.md', 'docs.connecting', 'docs.connecting-summary'],
  ['40-running-an-analysis.md', 'docs.running', 'docs.running-summary'],
  ['50-reading-the-findings.md', 'docs.findings', 'docs.findings-summary'],
  ['60-estimates-and-the-backlog.md', 'docs.estimates', 'docs.estimates-summary'],
  ['70-reports-and-exports.md', 'docs.reports', 'docs.reports-summary'],
  ['80-languages-and-settings.md', 'docs.languages', 'docs.languages-summary']
] as const;

/**
 * A guide in the reader's language, falling back to English.
 *
 * Per guide rather than per product, so a half translated documentation set shows the pages
 * that have been translated in the reader's language and the rest in English. Waiting until
 * every page is done would mean nobody ever sees any of it.
 */
async function fetchGuide(file: string, language: string): Promise<string> {
  if (language && language !== 'en') {
    const translated = await fetch(`/documentation/${language}/${file}`)
      .then((response) => (response.ok ? response.text() : ''))
      .catch(() => '');

    // A missing file on a static host is frequently the index page rather than a 404, so a
    // body that starts with a document is not a guide.
    if (translated.trim() && !translated.trimStart().startsWith('<!doctype')) return translated;
  }

  return fetch(`/documentation/${file}`)
    .then((response) => (response.ok ? response.text() : ''))
    .catch(() => '');
}

/**
 * The written documentation, searchable and in the reader's language.
 *
 * Rendered from the same markdown files that ship in the repository rather than paraphrased
 * into panels. A panel goes stale the first time somebody changes the product and forgets
 * this screen; a file cannot, because it is the file.
 *
 * The search reads the whole text of every guide, not only the titles. The question a
 * consultant actually has is "where does it say anything about rate limits", and a title
 * search cannot answer that.
 */
export function DocumentationPage() {
  const t = useT();
  const { language } = useLanguage();

  const [query, setQuery] = useState('');
  const [contents, setContents] = useState<Record<string, string>>({});
  const [selectedFile, setSelectedFile] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);

    Promise.all(guides.map(async ([file]) => [file, await fetchGuide(file, language)] as const))
      .then((documents) => {
        if (!active) return;
        setContents(Object.fromEntries(documents));
        setLoading(false);
      })
      .catch(() => active && setLoading(false));

    return () => { active = false; };
  }, [language]);

  const normalised = query.trim().toLowerCase();

  const visible = useMemo(
    () => guides.filter(([file, title, summary]) =>
      `${t(title)} ${t(summary)} ${contents[file] ?? ''}`.toLowerCase().includes(normalised)),
    [contents, normalised, t]);

  const selected = guides.find(([file]) => file === selectedFile);

  if (selected) {
    return (
      <section className="documentation-page">
        <article className="documentation-reader">
          <button className="back-link" type="button" onClick={() => setSelectedFile('')}>
            {t('docs.all-guides')}
          </button>

          <p className="eyebrow">{t(selected[1])}</p>

          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            components={{
              // A link to another guide opens it here rather than downloading the markdown,
              // which is what a plain anchor to a .md file does.
              a: ({ href, children }) => guides.some(([file]) => file === href)
                ? (
                  <button
                    className="documentation-inline-link"
                    type="button"
                    onClick={() => setSelectedFile(href!)}
                  >
                    {children}
                  </button>
                )
                : <a href={href} target="_blank" rel="noreferrer">{children}</a>,

              // Wrapped so a wide table scrolls inside itself instead of widening the page.
              table: ({ children }) => (
                <div className="documentation-table-wrap"><table>{children}</table></div>
              )
            }}
          >
            {contents[selected[0]] ?? ''}
          </ReactMarkdown>
        </article>
      </section>
    );
  }

  return (
    <section className="documentation-page">
      <div className="documentation-intro">
        <p className="eyebrow">{t('shell.documentation')}</p>
        <h2>{t('docs.heading')}</h2>
        <p>{t('docs.lede')}</p>

        <label className="field-label">
          {t('docs.find-a-guide')}
          <input
            type="search"
            value={query}
            placeholder={t('docs.search-placeholder')}
            onChange={(event) => { setQuery(event.target.value); setSelectedFile(''); }}
          />
          <span className="hint">{t('docs.search-hint')}</span>
        </label>
      </div>

      <div className="documentation-list">
        {visible.map(([file, title, summary]) => (
          <button className="documentation-row" key={file} type="button" onClick={() => setSelectedFile(file)}>
            <span>
              <strong>{t(title)}</strong>
              <small>{t(summary)}</small>
            </span>
            <span aria-hidden="true">&rarr;</span>
          </button>
        ))}
      </div>

      {loading && <p className="dashboard-empty">{t('docs.loading')}</p>}
      {!loading && visible.length === 0 && <p className="dashboard-empty">{t('docs.nothing-matches')}</p>}
    </section>
  );
}
