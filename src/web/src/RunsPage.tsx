import { useEffect, useState } from 'react';
import { useCan } from './access';
import { useT, useLanguage } from './i18n';
import { statusTag } from './tags';
import { getJson, sendJson, when, type EntityRead, type Run } from './workspace';
import { RunProgress } from './RunProgress';

/** A connection this engagement can read from. */
type Source = {
  connectionId: string;
  mode: string;
  name: string;
};

/** What the discovery detail returns for one run. */
type Discovery = {
  runId: string;
  entities: EntityRead[];
};

/** Every discovery, plan and apply, with what each one found. */
export function RunsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const canRun = useCan('Contributor');
  const { culture } = useLanguage();
  const [runs, setRuns] = useState<Run[] | null>(null);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Which run the page is watching. Set when one is started, and set by the list when
  // somebody opens a run that has not finished.
  const [watching, setWatching] = useState<string | null>(null);

  // What the engagement can read, and which of them this run will.
  //
  // Asked rather than guessed. This engagement has an uploaded export and a live
  // environment, and they produce different reports: the first is what a security review
  // lets you have in week one, the second is the one that can see run history and
  // privileges. Picking one on the reader's behalf would be picking what the report says.
  const [sources, setSources] = useState<Source[] | null>(null);
  const [source, setSource] = useState<string>('');

  async function load() {
    const result = await getJson<Run[]>(`/api/engagements/${engagementId}/runs`);
    setRuns(result.data ?? []);
    setError(result.error);
  }

  useEffect(() => { void load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [engagementId]);

  useEffect(() => {
    let cancelled = false;

    void getJson<Source[]>(`/api/engagements/${engagementId}/connections`).then((result) => {
      if (cancelled) return;

      // Azure DevOps is a place to publish to, not a place to read from, and offering it
      // here would be offering a run that cannot do anything.
      const readable = (result.data ?? []).filter((one) => one.mode !== 'azureDevOps');

      setSources(readable);

      // A live environment first when there is one. It is the mode that can see run
      // history and privileges, so it is the one somebody means by "discover" unless they
      // say otherwise.
      setSource(readable.find((one) => one.mode !== 'offlineZip')?.connectionId
        ?? readable[0]?.connectionId
        ?? '');
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  // A run the page has not been told about can still be going: somebody started it on
  // another machine, or the browser was closed and reopened. Picking the newest unfinished
  // one up means the timeline is there when they come back to it.
  useEffect(() => {
    if (watching || !runs) return;

    const moving = runs.find((run) => run.status === 'pending' || run.status === 'running'
      || run.status === 'awaitingSelection');

    if (moving) setWatching(moving.runId);
  }, [runs, watching]);

  async function startDiscovery() {
    setStarting(true);

    // 'assessment', not 'discover'. This page was ported from the migrator, where discover
    // is one of five real modes; here the four are quickScan, assessment, publish and
    // compare. The database refused every insert on a check constraint, the API threw, and
    // the button did nothing at all, visibly, from the day it was written.
    const result = await sendJson<{ runId: string }>(
      `/api/engagements/${engagementId}/runs`, 'POST',
      { mode: 'assessment', sourceConnectionId: source });

    if (result.error) setError(result.error);

    // Opened straight away. The run stops within seconds to ask which solutions to read, and
    // a button that queues something invisible is the thing this page is being fixed for.
    if (result.data?.runId) setWatching(result.data.runId);

    await load();
    setStarting(false);
  }

  if (!runs) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  return (
    <div className="runs-page">
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
            <span className="run-source">
              {sources && sources.length > 1 && (
                <label className="run-source-pick">
                  <span>{t('runs.read-from')}</span>
                  <select value={source} onChange={(event) => setSource(event.target.value)}>
                    {sources.map((one) => (
                      <option key={one.connectionId} value={one.connectionId}>
                        {one.name} · {t('source.' + one.mode)}
                      </option>
                    ))}
                  </select>
                </label>
              )}

              <button
                type="button"
                className="primary-button"
                disabled={starting || !source}
                onClick={() => void startDiscovery()}>
                {t('runs.mode.discover')}
              </button>
            </span>
          )}
        </div>

        {/* Only discovery can be started from here. A plan needs a target connection and an
            apply needs an approval, and offering a button that cannot work is worse than
            not offering one. */}
        <p className="panel-note">{t('runs.writes-nothing')}</p>

        {/* Said rather than left to the button being grey. A disabled control with no
            explanation is the same support call as one that does nothing. */}
        {canRun && sources?.length === 0 && (
          <p className="curation-message danger">{t('runs.nothing-to-read')}</p>
        )}

        {error && <p className="curation-message danger">{error}</p>}
      </section>

      {watching && (
        <section className="panel">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{t('runs.pipeline')}</p>
              <h2>{t('runs.stages')}</h2>
            </div>
            <button type="button" className="text-button" onClick={() => setWatching(null)}>
              {t('runs.stop-watching')}
            </button>
          </div>

          <RunProgress runId={watching} onChanged={() => void load()} />
        </section>
      )}

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
              <RunRow
                engagementId={engagementId}
                run={run}
                key={run.runId}
                onWatch={() => setWatching(run.runId)}
                onRemoved={() => {
                  if (watching === run.runId) setWatching(null);
                  void load();
                }} />
            ))}
          </div>
        )}
      </section>
    </div>
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
function RunRow({ engagementId, run, onWatch, onRemoved }: {
  engagementId: string;
  run: Run;
  onWatch: () => void;
  onRemoved: () => void;
}) {
  const t = useT();
  const { culture } = useLanguage();
  const [found, setFound] = useState<EntityRead[] | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open || found) return;

    let cancelled = false;

    void getJson<Discovery>(`/api/engagements/${engagementId}/runs/${run.runId}/discovery`)
      .then((result) => { if (!cancelled) setFound(result.data?.entities ?? []); });

    return () => { cancelled = true; };
  }, [open, found, engagementId, run.runId]);

  const total = found?.reduce((sum, entity) => sum + (entity.recordCount ?? 0), 0) ?? 0;

  return (
    <details className="run-row" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>
        <span className="run-mode">
          {t('runs.mode.' + run.mode)}
          <span className={`tag ${run.writes ? 'danger' : 'complete'}`}>
            {run.writes ? t('runs.writes-to-the-target') : t('runs.writes-nothing')}
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

        <p className="panel-note run-row-actions">
          <button type="button" className="text-button" onClick={onWatch}>{t('runs.watch-this-run')}</button>
          <RemoveRun runId={run.runId} status={run.status} onRemoved={onRemoved} />
        </p>

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
                    <tr key={`${entity.componentTypeId}-${entity.evidenceSource}`}>
                      <th scope="row">{entity.componentTypeId}</th>
                      <td>{entity.recordCount ?? '—'}</td>
                      <td><span className="tag muted">{t('source.' + entity.evidenceSource)}</span></td>
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

/**
 * Removing a run and everything it produced.
 *
 * Two steps, because it cannot be undone and because this repository has already deleted
 * something of a client's by accident once. The confirmation says what goes and, more
 * importantly, what does not: work items already written to a board stay exactly where they
 * are and only this product's record of having written them is removed.
 *
 * Admin only on the server. The button is shown to anybody who can see the run and the
 * refusal explains itself, because hiding it would leave a consultant wondering how the
 * list is supposed to be tidied.
 */
function RemoveRun({ runId, status, onRemoved }: {
  runId: string;
  status: string;
  onRemoved: () => void;
}) {
  const t = useT();
  const canRemove = useCan('Admin');
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!canRemove) return null;

  // A worker part way through would carry on writing findings against a run that no longer
  // exists, so the server refuses and the button says so rather than offering it.
  const moving = status === 'running' || status === 'pending';

  async function remove() {
    setBusy(true);
    const result = await sendJson(`/api/runs/${runId}`, 'DELETE');
    setError(result.error);
    setBusy(false);

    if (!result.error) {
      setConfirming(false);
      onRemoved();
    }
  }

  if (confirming) {
    return (
      <span className="stage-confirm">
        <span>{t('runs.remove-warning')}</span>
        <button type="button" className="text-button" disabled={busy} onClick={() => void remove()}>
          {busy ? t('runs.removing') : t('runs.yes-remove-it')}
        </button>
        <button type="button" className="text-button" disabled={busy} onClick={() => setConfirming(false)}>
          {t('common.cancel')}
        </button>
        {error && <span className="stage-error">{error}</span>}
      </span>
    );
  }

  return (
    <>
      <button
        type="button"
        className="text-button danger-button"
        disabled={moving}
        title={moving ? t('runs.cannot-remove-while-running') : undefined}
        onClick={() => setConfirming(true)}>
        {t('runs.remove-this-run')}
      </button>
      {error && <span className="stage-error">{error}</span>}
    </>
  );
}
