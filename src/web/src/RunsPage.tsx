import { useEffect, useState } from 'react';
import { useCan } from './access';
import { useT, useLanguage } from './i18n';
import { statusTag } from './tags';
import { getJson, sendJson, when, type Run } from './workspace';

/** One entity as a discovery found it. */
type DiscoveredEntity = {
  canonicalEntityId: string;
  declaredLevel: string;
  recordCount: number;
  succeeded: boolean;
  error: string | null;
};

/** Which modes touch a client's environment. The distinction is the product's whole safety story. */
const writes: Record<string, boolean> = {
  discover: false,
  plan: false,
  verify: false,
  apply: true,
  rollback: true
};

/** Every discovery, plan and apply, with what each one found. */
export function RunsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const canRun = useCan('Contributor');
  const { culture } = useLanguage();
  const [runs, setRuns] = useState<Run[] | null>(null);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    const result = await getJson<Run[]>(`/api/engagements/${engagementId}/runs`);
    setRuns(result.data ?? []);
    setError(result.error);
  }

  useEffect(() => { void load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [engagementId]);

  async function startDiscovery() {
    setStarting(true);
    const result = await sendJson<Run>(`/api/engagements/${engagementId}/runs`, 'POST', { mode: 'discover' });
    if (result.error) setError(result.error);
    await load();
    setStarting(false);
  }

  if (!runs) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  return (
    <>
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.runs')}</p>
            <h2>{t('runs.start-a-run')}</h2>
          </div>
          {/* A Viewer reads what other people found. Offering them a button that comes
              back forbidden reads as the product being broken rather than as the role
              working, which is a support call either way. */}
          {canRun && (
            <button type="button" className="primary-button" disabled={starting} onClick={() => void startDiscovery()}>
              {t('runs.mode.discover')}
            </button>
          )}
        </div>

        {/* Only discovery can be started from here. A plan needs a target connection and an
            apply needs an approval, and offering a button that cannot work is worse than
            not offering one. */}
        <p className="panel-note">{t('runs.writes-nothing')}</p>
        {error && <p className="curation-message danger">{error}</p>}
      </section>

      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.runs')}</p>
            <h2>{t('view.runs')}</h2>
          </div>
        </div>

        {runs.length === 0 ? (
          <p className="dashboard-empty">{t('runs.no-runs-yet')}</p>
        ) : (
          <div className="run-list">
            {runs.map((run) => (
              <RunRow engagementId={engagementId} run={run} key={run.runId} />
            ))}
          </div>
        )}
      </section>
    </>
  );
}

/**
 * One run, and what it found when you open it.
 *
 * The list said a discovery finished, which is not the same as saying what it read. A
 * consultant looking at a finished run wants the second, and until now the only way to see it
 * was to open the assessment and hope it came from this run rather than a later one.
 *
 * Fetched when the row is opened rather than with the list. An engagement with forty runs
 * would otherwise make forty requests to draw a page nobody has expanded yet.
 */
function RunRow({ engagementId, run }: { engagementId: string; run: Run }) {
  const t = useT();
  const { culture } = useLanguage();
  const [found, setFound] = useState<DiscoveredEntity[] | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open || found) return;

    let cancelled = false;

    getJson<DiscoveredEntity[]>(`/api/engagements/${engagementId}/runs/${run.runId}/discovery`)
      .then((result) => { if (!cancelled) setFound(result.data ?? []); });

    return () => { cancelled = true; };
  }, [open, found, engagementId, run.runId]);

  const total = found?.reduce((sum, entity) => sum + entity.recordCount, 0) ?? 0;

  return (
    <details className="run-row" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>
        <span className="run-mode">
          {t('runs.mode.' + run.mode)}
          <span className={`tag ${writes[run.mode] ? 'danger' : 'complete'}`}>
            {writes[run.mode] ? t('runs.writes-to-the-target') : t('runs.writes-nothing')}
          </span>
        </span>

        <span className={`tag ${statusTag[run.status] ?? 'muted'}`}>
          {t('runs.status.' + run.status)}
        </span>

        <span className="run-when">
          {when(run.createdUtc, culture, t('common.never'))}
          <span className="hint">{run.createdBy}</span>
        </span>
      </summary>

      <div className="run-body">
        {run.error && <p className="curation-message danger">{run.error}</p>}

        {found === null ? (
          <p className="dashboard-empty">{t('common.loading')}</p>
        ) : found.length === 0 ? (
          <p className="panel-note">{t('runs.nothing-recorded')}</p>
        ) : (
          <>
            <p className="panel-note">
              {t('runs.read-in-total', String(total), String(found.length))}
            </p>

            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{t('common.entity')}</th>
                    <th scope="col">{t('common.records')}</th>
                    <th scope="col">{t('runs.declared-level')}</th>
                    <th scope="col">{t('common.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {found.map((entity) => (
                    <tr key={entity.canonicalEntityId}>
                      <th scope="row">{entity.canonicalEntityId}</th>
                      <td>{entity.recordCount}</td>
                      <td><span className="tag muted">{entity.declaredLevel}</span></td>
                      <td className="wrapping-cell">
                        {entity.succeeded
                          ? <span className="tag complete">{t('runs.status.succeeded')}</span>
                          : <span className="tag danger">{entity.error ?? t('runs.status.failed')}</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </details>
  );
}
