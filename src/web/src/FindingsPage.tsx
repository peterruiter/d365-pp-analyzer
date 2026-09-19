import { useEffect, useMemo, useState } from 'react';
import { useT } from './i18n';
import { getJson, sendJson } from './workspace';

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
  const [severity, setSeverity] = useState<string>('all');
  const [category, setCategory] = useState<string>('all');
  const [open, setOpen] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    void getJson<FindingsResponse>(`/api/engagements/${engagementId}/findings`).then((result) => {
      if (cancelled) return;
      if (result.data) setData(result.data);
      else setError(result.error ?? 'The findings could not be read.');
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  const categories = useMemo(
    () => [...new Set((data?.findings ?? []).map(finding => finding.category))].sort(),
    [data]);

  const visible = useMemo(() => (data?.findings ?? [])
    .filter(finding => severity === 'all' || finding.severity === severity)
    .filter(finding => category === 'all' || finding.category === category)
    .sort((a, b) => severityOrder.indexOf(a.severity) - severityOrder.indexOf(b.severity)),
    [data, severity, category]);

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

        <span className="filter-count">
          {t('findings.showing', String(visible.length), String(data.findings.length))}
        </span>
      </section>

      <table className="findings-table">
        <thead>
          <tr>
            <th>{t('findings.severity')}</th>
            <th>{t('findings.finding')}</th>
            <th>{t('findings.component')}</th>
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
                  {finding.ruleName}
                  {/* A finding on a managed component is somebody else's to fix, and saying so
                      in the row saves a conversation that otherwise happens in the workshop. */}
                  {finding.isManaged && <span className="tag">{t('findings.managed')}</span>}
                </td>
                <td data-label={t('findings.component')}>{finding.componentName ?? t('findings.solution-wide')}</td>
                <td className="numeric" data-label={t('findings.hours')}>{finding.lowHours}–{finding.highHours}</td>
                <td data-label={t('findings.estimate-from')}>
                  {t(`estimate.source.${finding.estimateLayer}`)}
                  {finding.confidence === 'low' && <span className="tag tag-warning">{t('findings.low-confidence')}</span>}
                  {finding.flaggedReason && <span className="tag tag-warning">{t('findings.flagged')}</span>}
                </td>
              </tr>

              {open === finding.findingId && (
                <tr className="detail-row" key={`${finding.findingId}-detail`}>
                  <td colSpan={5}>
                    <div className="finding-detail">
                      <h3>{t('evidence.heading')}</h3>
                      <p className="muted">{t('evidence.explain')}</p>
                      <dl>
                        {Object.entries(finding.evidence).map(([key, value]) => (
                          <div key={key}><dt>{key}</dt><dd>{value}</dd></div>
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

      {/*
        Its own section rather than a footnote. Everything above is a number and this is the
        shape of the hole around them.
      */}
      <section className="not-assessed">
        <h2>{t('findings.not-assessed-title', String(data.notAssessed.length), String(data.ruleCount))}</h2>
        <p className="muted">{t('findings.not-assessed-explain')}</p>
        <ul>
          {data.notAssessed.map(entry => (
            <li key={entry.ruleId}><code>{entry.ruleId}</code> {entry.reason}</li>
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
  const [low, setLow] = useState(String(finding.lowHours));
  const [high, setHigh] = useState(String(finding.highHours));
  const [rationale, setRationale] = useState('');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const valid = Number(low) <= Number(high) && rationale.trim().length > 0;

  async function save() {
    setSaving(true);

    try {
      const result = await sendJson(`/api/engagements/${engagementId}/overrides`, 'POST', {
        scope: 'finding',
        findingKey: finding.stableKey,
        lowHours: Number(low),
        highHours: Number(high),
        rationale: rationale.trim()
      });

      if (!result.error) setSaved(true);
    } finally {
      setSaving(false);
    }
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

      {/* Said on the form rather than discovered on the next run. */}
      <p className="muted">{t('estimate.override.survives-rerun')}</p>
    </div>
  );
}
