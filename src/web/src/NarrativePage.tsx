import { useEffect, useState } from 'react';
import { useT, type Translate } from './i18n';
import { getJson, sendJson } from './workspace';
import { useCan } from './access';
import { MaturityPanel } from './MaturityPanel';

/** A report section somebody has to write, as the report model declares it. */
type Section = {
  id: string;
  name: string;
  kind: string;
  prompt: string | null;
  context: string | null;
};

/** What has been written for one of them. */
type Written = {
  sectionId: string;
  body: string;
  updatedUtc: string;
  updatedByName: string;
};

/**
 * One section, its prompt, and somewhere to type.
 *
 * Saved on leaving the box rather than on every keystroke. A keystroke save on a text area
 * somebody is composing a paragraph in is a request per character and a save history nobody
 * wants; a save button is a thing people forget to press and then lose a workshop's notes to.
 */
function SectionEditor({ section, existing, engagementId, readOnly, t, onSaved }: {
  section: Section;
  existing: Written | undefined;
  engagementId: string;
  readOnly: boolean;
  t: Translate;
  onSaved: (sectionId: string, body: string) => void;
}) {
  const [body, setBody] = useState(existing?.body ?? '');
  const [state, setState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);

  // The saved text can arrive after this component has mounted, and a controlled textarea
  // that ignores it would show an empty box over text that is already stored.
  useEffect(() => { setBody(existing?.body ?? ''); }, [existing?.body]);

  async function save() {
    if (readOnly) return;
    if ((existing?.body ?? '') === body.trim()) return;

    setState('saving');
    setError(null);

    const result = await sendJson(
      `/api/engagements/${engagementId}/narrative/${section.id}`, 'PUT', { body: body.trim() });

    if (result.error) {
      setState('error');
      setError(result.error);
      return;
    }

    setState('saved');
    onSaved(section.id, body.trim());
  }

  return (
    <section className="panel narrative-section">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">
            {t(section.kind === 'hybrid' ? 'narrative.kind-hybrid' : 'narrative.kind-written')}
          </p>
          <h2>{section.name}</h2>
        </div>

        {existing && (
          <span className="tag muted">
            {t('narrative.written-by', existing.updatedByName)}
          </span>
        )}
      </div>

      <div className="narrative-body">
        {/*
          The prompt is the question the section answers, and it stays on the screen while
          somebody types rather than sitting in a tooltip. It is also what the report prints
          in place of the text when nobody has written any, so the two agree.
        */}
        {section.prompt && <p className="narrative-prompt">{section.prompt}</p>}
        {section.context && <p className="hint">{section.context}</p>}

        <textarea
          className="narrative-input"
          value={body}
          readOnly={readOnly}
          rows={8}
          placeholder={readOnly ? t('narrative.read-only') : t('narrative.placeholder')}
          onChange={(event) => { setBody(event.target.value); setState('idle'); }}
          onBlur={() => void save()}
        />

        <div className="narrative-foot">
          <span className="hint">
            {state === 'saving' ? t('narrative.saving')
              : state === 'saved' ? t('narrative.saved')
                : state === 'error' ? (error ?? t('narrative.not-saved'))
                  : readOnly ? t('narrative.read-only')
                    : t('narrative.saves-when-you-leave')}
          </span>

          {!readOnly && (
            <button
              type="button"
              className="secondary-button"
              disabled={state === 'saving'}
              onClick={() => void save()}
            >
              {t('narrative.save')}
            </button>
          )}
        </div>
      </div>
    </section>
  );
}

/**
 * The half of an assessment that comes from talking to people.
 *
 * Nothing on this screen is generated and nothing ever will be. An estate cannot say whether
 * a team is ready for change, what the client was trying to achieve, or which three
 * scenarios they are choosing between; those come from interviews, and the product's job is
 * to hold them next to the numbers rather than to invent them.
 *
 * Which sections exist, and the prompt for each, come from report-model.json. The report
 * prints the same prompt where a section has not been written, so the question somebody is
 * answering here is the question the document asks.
 */
export function NarrativePage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const canWrite = useCan('Contributor');

  const [sections, setSections] = useState<Section[] | null>(null);
  const [written, setWritten] = useState<Written[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const [sectionResult, writtenResult] = await Promise.all([
        getJson<Section[]>('/api/report-sections'),
        getJson<Written[]>(`/api/engagements/${engagementId}/narrative`)
      ]);

      if (cancelled) return;

      setSections(sectionResult.data ?? []);
      setWritten(writtenResult.data ?? []);
      setError(sectionResult.error ?? writtenResult.error);
    }

    void load();
    return () => { cancelled = true; };
  }, [engagementId]);

  if (error) return <p className="error">{error}</p>;

  if (!sections) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  const readOnly = !canWrite;

  function remember(sectionId: string, body: string) {
    setWritten((current) => {
      const rest = current.filter((entry) => entry.sectionId !== sectionId);

      if (body === '') return rest;

      return [...rest, {
        sectionId,
        body,
        updatedUtc: new Date().toISOString(),
        updatedByName: current.find((entry) => entry.sectionId === sectionId)?.updatedByName ?? ''
      }];
    });
  }

  return (
    <>
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.narrative')}</p>
            <h2>{t('narrative.heading')}</h2>
          </div>
          <span className="tag muted">
            {t('narrative.n-of-m', written.length, sections.length)}
          </span>
        </div>

        <p className="panel-note">{t('narrative.lede')}</p>
      </section>

      {sections.map((section) => (
        <div key={section.id}>
          <SectionEditor
            section={section}
            existing={written.find((entry) => entry.sectionId === section.id)}
            engagementId={engagementId}
            readOnly={readOnly}
            t={t}
            onSaved={remember}
          />

          {/* The scores belong with the paragraph that explains them. */}
          {section.id === 'functionalMaturity' && (
            <MaturityPanel engagementId={engagementId} readOnly={readOnly} />
          )}
        </div>
      ))}
    </>
  );
}
