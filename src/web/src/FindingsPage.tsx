import { useEffect, useMemo, useState } from 'react';
import { useT, useLanguage } from './i18n';
import { useCan } from './access';
import { getJson, sendJson, when, type Run } from './workspace';

/**
 * One finding, as the API returns it.
 *
 * The estimate travels with the finding rather than being fetched separately, because every
 * screen that shows one shows the other and two round trips would let them disagree on screen
 * while somebody is reading them.
 */
interface Finding {
  findingId: string;
  stableKey: string;
  ruleId: string;
  ruleName: string;
  category: string;
  severity: 'critical' | 'high' | 'medium' | 'low' | 'info';
  componentName: string | null;
  componentType: string | null;
  solutionName: string | null;
  isManaged: boolean;
  evidence: Record<string, string>;
  lowHours: number;
  highHours: number;
  storyPoints: number | null;
  confidence: 'high' | 'medium' | 'low';
  estimateLayer: 'engagementOverride' | 'model' | 'bandDefault';
  rationale: string;
  flaggedReason: string | null;
  falsePositive: string | null;
  why: string;
  recommendation: string;
}

interface NotAssessed {
  ruleId: string;

  /** The rule's name, in the reader's language. The identifier is for a consultant, not a client. */
  ruleName: string;

  /** A finished sentence, not a lookup key. Composed by the API from the key and the evidence it needed. */
  reason: string;
  missingEvidence: string | null;
}

interface FindingsResponse {
  findings: Finding[];
  notAssessed: NotAssessed[];
  ruleCount: number;
  caveats: string[];
}

const severityOrder = ['critical', 'high', 'medium', 'low', 'info'];

export function FindingsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const [data, setData] = useState<FindingsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { culture } = useLanguage();
  const [severity, setSeverity] = useState<string>('all');
  const [category, setCategory] = useState<string>('all');
  const [solution, setSolution] = useState<string>('all');
  const [open, setOpen] = useState<string | null>(null);

  /**
   * Which run is being read, and which runs there are to choose from.
   *
   * The API has always taken a runId and defaulted to the latest that reached scoring. That
   * is the right default and it was the only option: an engagement accumulates a run per
   * attempt, and comparing what a re-run found against what the last one did meant reading
   * two reports side by side.
   */
  const [runId, setRunId] = useState<string | null>(null);
  const [runs, setRuns] = useState<Run[]>([]);

  useEffect(() => {
    let cancelled = false;
    const query = runId === null ? '' : `?runId=${runId}`;

    void getJson<FindingsResponse>(`/api/engagements/${engagementId}/findings${query}`).then((result) => {
      if (cancelled) return;
      if (result.data) setData(result.data);
      else setError(result.error ?? 'The findings could not be read.');
    });

    return () => { cancelled = true; };
  }, [engagementId, runId]);

  // Only the runs that produced something. A run that died in extraction has no findings to
  // show and offering it is offering an empty page with no explanation.
  useEffect(() => {
    let cancelled = false;

    void getJson<Run[]>(`/api/engagements/${engagementId}/runs`).then((result) => {
      if (cancelled || !result.data) return;

      setRuns(result.data.filter((run) =>
        run.status === 'succeeded' || run.status === 'partial'));
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  const categories = useMemo(
    () => [...new Set((data?.findings ?? []).map(finding => finding.category))].sort(),
    [data]);

  // Every solution the run actually found something in, plus a bucket for the findings that
  // are about the estate rather than about one solution.
  const solutions = useMemo(
    () => [...new Set((data?.findings ?? []).map(finding => finding.solutionName ?? ''))].sort(),
    [data]);

  const visible = useMemo(() => (data?.findings ?? [])
    .filter(finding => severity === 'all' || finding.severity === severity)
    .filter(finding => category === 'all' || finding.category === category)
    .filter(finding => solution === 'all' || (finding.solutionName ?? '') === solution)
    .sort((a, b) => severityOrder.indexOf(a.severity) - severityOrder.indexOf(b.severity)),
    [data, severity, category, solution]);

  if (error) return <p className="error">{error}</p>;
  if (!data) return <p className="lede">{t('findings.loading')}</p>;

  return (
    // Not "workspace". That is the shell's own class and it is display:flex, so every
    // section on this page became a column in a row: the filters, the table and the
    // not-assessed block sat side by side, and the last of them was squeezed into a
    // eight-character-wide strip of text down the right hand edge.
    <div className="findings-page">
      {/*
        The caveats sit above the findings, not below them. Somebody who reads a short list
        without knowing a third of the checks never ran concludes the estate is clean, and this
        is the one screen where that conclusion gets made.
      */}
      {data.caveats.length > 0 && (
        <section className="callout callout-warning">
          <h2>{t('findings.before-you-read-this')}</h2>
          <ul>{data.caveats.map(caveat => <li key={caveat}>{caveat}</li>)}</ul>
        </section>
      )}

      <section className="filters">
        <label>
          {t('findings.severity')}
          <select value={severity} onChange={event => setSeverity(event.target.value)}>
            <option value="all">{t('findings.all')}</option>
            {severityOrder.map(level => <option key={level} value={level}>{t(`severity.${level}`)}</option>)}
          </select>
        </label>

        <label>
          {t('findings.category')}
          <select value={category} onChange={event => setCategory(event.target.value)}>
            <option value="all">{t('findings.all')}</option>
            {categories.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>

        {/* Only where there is a choice to make. One run and one solution is the common
            case and a select with a single option in it is furniture. */}
        {solutions.length > 1 && (
          <label>
            {t('findings.solution')}
            <select value={solution} onChange={event => setSolution(event.target.value)}>
              <option value="all">{t('findings.all')}</option>
              {solutions.map(name => (
                <option key={name} value={name}>{name === '' ? t('findings.no-solution') : name}</option>
              ))}
            </select>
          </label>
        )}

        {runs.length > 1 && (
          <label>
            {t('findings.run')}
            <select
              value={runId ?? ''}
              onChange={event => { setRunId(event.target.value === '' ? null : event.target.value); setOpen(null); }}>
              <option value="">{t('findings.latest-run')}</option>
              {runs.map(run => (
                <option key={run.runId} value={run.runId}>
                  {when(run.completedUtc ?? run.createdUtc, culture, '')} · {t(`runs.mode.${run.mode}`)}
                </option>
              ))}
            </select>
          </label>
        )}

        <span className="filter-count">
          {t('findings.showing', String(visible.length), String(data.findings.length))}
        </span>
      </section>

      {/*
        Wrapped, because a table is the one thing on a page that refuses to be narrower than
        its contents. Without this the table pushed the whole page sideways and the detail
        under a finding ran off the right of the window mid-sentence. The wrapper scrolls its
        own overflow; the rule that lets it is the min-width on the page's grid children.
      */}
      {/* On a surface, like every other table in the product. It sat directly on the page
          background, which made the rows look like loose text rather than a table and left
          the severity tint with nothing to tint against. */}
      <section className="panel">
      <div className="table-wrap">
      <table className="findings-table">
        <thead>
          <tr>
            <th>{t('findings.severity')}</th>
            <th>{t('findings.finding')}</th>
            <th>{t('findings.component')}</th>
            <th>{t('findings.solution')}</th>
            <th className="numeric">{t('findings.hours')}</th>
            <th>{t('findings.estimate-from')}</th>
          </tr>
        </thead>
        <tbody>
          {visible.map(finding => (
            <>
              <tr
                key={finding.findingId}
                className={`severity-${finding.severity} ${open === finding.findingId ? 'open' : ''}`}
                aria-expanded={open === finding.findingId}
                onClick={() => setOpen(open === finding.findingId ? null : finding.findingId)}
              >
                {/*
                  Each cell carries its own column heading. On a phone the table reflows into
                  a stack of rows and the header disappears with it, so without this a reader
                  gets four unlabelled values under a finding name.
                */}
                <td data-label={t('findings.severity')}>
                  <span className={`severity-pill severity-${finding.severity}`}>{t(`severity.${finding.severity}`)}</span>
                </td>
                <td data-label={t('findings.finding')}>
                  {/*
                    The row opens. Nothing said so, so a reader saw a table of rule names
                    with no explanation anywhere and concluded the product had not produced
                    one: the evidence, the reason and the recommendation were all one click
                    away behind no affordance at all.
                  */}
                  <span className="finding-name">
                    <span className="finding-caret" aria-hidden="true" />
                    <span>
                      {finding.ruleName}
                      {/* A finding on a managed component is somebody else's to fix, and
                          saying so in the row saves a conversation that otherwise happens
                          in the workshop. */}
                      {finding.isManaged && <span className="tag">{t('findings.managed')}</span>}

                      {/* One line of why, in the row. The full reason, the evidence and the
                          recommendation are inside. */}
                      <small>{finding.why}</small>
                    </span>
                  </span>
                </td>
                <td data-label={t('findings.component')}>
                  {finding.componentName ?? t('findings.solution-wide')}
                  {finding.componentType && <small>{finding.componentType}</small>}
                </td>
                <td data-label={t('findings.solution')}>
                  {finding.solutionName ?? t('findings.no-solution')}
                </td>
                <td className="numeric" data-label={t('findings.hours')}>{finding.lowHours}–{finding.highHours}</td>
                <td data-label={t('findings.estimate-from')}>
                  {t(`estimate.source.${finding.estimateLayer}`)}
                  {finding.confidence === 'low' && <span className="tag tag-warning">{t('findings.low-confidence')}</span>}
                  {finding.flaggedReason && <span className="tag tag-warning">{t('findings.flagged')}</span>}
                </td>
              </tr>

              {open === finding.findingId && (
                <tr className="detail-row" key={`${finding.findingId}-detail`}>
                  <td colSpan={6}>
                    <div className="finding-detail">
                      <h3>{t('evidence.heading')}</h3>
                      <p className="muted">{t('evidence.explain')}</p>
                      <dl>
                        {Object.entries(finding.evidence).map(([key, value]) => (
                          /*
                            Rendered through String(), because React draws false as nothing
                            at all. The evidence for "cloud flow with no failure path" is
                            hasFailurePath: false, and the screen showed the label with an
                            empty space beside it: the single most important piece of
                            evidence on the finding, displayed as though it were missing.
                          */
                          <div key={key}>
                            <dt>{key}</dt>
                            <dd>{value === null || value === undefined ? '—' : String(value)}</dd>
                          </div>
                        ))}
                      </dl>

                      <h3>{t('findings.why-it-matters')}</h3>
                      <p>{finding.why}</p>

                      <h3>{t('findings.recommendation')}</h3>
                      <p>{finding.recommendation}</p>

                      {/*
                        Shown before the estimate, deliberately. Where a rule is known to be
                        wrong is the thing to read before quoting the finding at a client, not
                        after they have disproved it in the room.
                      */}
                      {finding.falsePositive && (
                        <div className="callout callout-warning">
                          <h3>{t('findings.before-you-act')}</h3>
                          <p>{finding.falsePositive}</p>
                        </div>
                      )}

                      <h3>{t('estimate.rationale')}</h3>
                      <p>{finding.rationale}</p>
                      {finding.flaggedReason && <p className="warning">{finding.flaggedReason}</p>}

                      <OverrideForm engagementId={engagementId} finding={finding} />
                    </div>
                  </td>
                </tr>
              )}
            </>
          ))}
        </tbody>
      </table>
      </div>
      </section>

      {/*
        Its own section rather than a footnote. Everything above is a number and this is the
        shape of the hole around them.
      */}
      <section className="not-assessed">
        <h2>{t('findings.not-assessed-title', String(data.notAssessed.length), String(data.ruleCount))}</h2>
        <p className="muted">{t('findings.not-assessed-explain')}</p>
        <ul>
          {data.notAssessed.map(entry => (
            <li key={entry.ruleId}>
              {/* The rule's name and the sentence, both from the API in the reader's
                  language. This was the identifier and the lookup key side by side, which
                  is the product talking to itself on the page a careful reader reads
                  first. */}
              <strong>{entry.ruleName}</strong>
              <span>{entry.reason}</span>
              <code>{entry.ruleId}</code>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

/**
 * Sets an estimate for this engagement.
 *
 * The rationale is required by the form because it is required by the database and by the
 * report. An override with no reason is indistinguishable from a typo three months later, and
 * it is the number that ends up in the statement of work.
 */
function OverrideForm({ engagementId, finding }: { engagementId: string; finding: Finding }) {
  const t = useT();
  const canContribute = useCan('Contributor');
  const [low, setLow] = useState(String(finding.lowHours));
  const [high, setHigh] = useState(String(finding.highHours));
  const [rationale, setRationale] = useState('');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const range = Number(low) <= Number(high);
  const reasoned = rationale.trim().length > 0;
  const valid = range && reasoned;

  async function save() {
    setSaving(true);
    setError(null);

    try {
      const result = await sendJson(`/api/engagements/${engagementId}/overrides`, 'POST', {
        scope: 'finding',
        findingKey: finding.stableKey,
        lowHours: Number(low),
        highHours: Number(high),
        rationale: rationale.trim()
      });

      // Said out loud. This used to set saved on success and do nothing at all otherwise,
      // so a refusal the API had explained in a sentence arrived as a button that did
      // nothing when pressed.
      if (result.error) setError(result.error);
      else setSaved(true);
    } finally {
      setSaving(false);
    }
  }

  // A viewer cannot save one, and the endpoint refuses it. Saying so beats offering a form
  // that fills in and then will not submit.
  if (!canContribute) {
    return (
      <div className="override-form">
        <h3>{t('estimate.override')}</h3>
        <p className="muted">{t('estimate.override.needs-contributor')}</p>
      </div>
    );
  }

  return (
    <div className="override-form">
      <h3>{t('estimate.override')}</h3>

      <div className="field-row">
        <label>{t('estimate.low')}<input value={low} onChange={event => setLow(event.target.value)} inputMode="decimal" /></label>
        <label>{t('estimate.high')}<input value={high} onChange={event => setHigh(event.target.value)} inputMode="decimal" /></label>
      </div>

      <label className="full">
        {t('estimate.rationale')}
        <textarea
          value={rationale}
          onChange={event => setRationale(event.target.value)}
          placeholder={t('estimate.override.rationaleRequired')}
          rows={3}
        />
      </label>

      <button className="primary-button" disabled={!valid || saving} onClick={save}>
        {saved ? t('estimate.override.saved') : t('estimate.override.save')}
      </button>

      {!valid && (
        <p className="blocked">
          {!range ? t('estimate.override.not-a-range') : t('estimate.override.rationaleRequired')}
        </p>
      )}

      {error && <p className="error">{error}</p>}

      {/* Said on the form rather than discovered on the next run. */}
      <p className="muted">{t('estimate.override.survives-rerun')}</p>
    </div>
  );
}
