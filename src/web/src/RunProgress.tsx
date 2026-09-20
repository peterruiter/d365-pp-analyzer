import { useCallback, useEffect, useState } from 'react';
import { useCan } from './access';
import { useT, useLanguage, type Translate } from './i18n';
import { statusTag } from './tags';
import { getJson, sendJson, when, type Run } from './workspace';

/** One stage of the pipeline, as the run detail returns it. */
export type RunStage = {
  stageId: string;
  name: string;
  description: string;
  order: number;
  status: string;
  attempt: number;
  startedUtc: string | null;
  completedUtc: string | null;
  error: string | null;

  /** What the stage is doing right now, as {"key","args"} JSON. Only ever set while it runs. */
  progress: string | null;
  retryable: boolean;
};

/** A run with every stage, rather than a run with a list of the finished ones. */
export type RunDetail = {
  run: Run;
  stages: RunStage[];
  stagesComplete: number;
  stagesTotal: number;
  currentStageName: string | null;
};

/** One solution the run found, and whether it is ticked. */
type RunSolution = {
  uniqueName: string;
  friendlyName: string | null;
  version: string | null;
  isManaged: boolean;
  publisherPrefix: string | null;
  publisherName: string | null;
  componentCount: number | null;
  isFirstParty: boolean;
  selected: boolean;
};

/** What the picker has to offer, and what it starts with ticked. */
type Choice = {
  runId: string;
  mode: string;
  awaiting: boolean;
  solutions: RunSolution[];
  checks: {
    solutionChecker: boolean;
    modelEstimates: boolean;
    environmentHealth: boolean;
    exportSolutions: boolean;
  };

  /** Where the checker may run, or null when nobody has chosen. Null means it will refuse. */
  checkerGeography: string | null;
};

/** The statuses that mean the worker has not finished, so the page keeps asking. */
const Moving = new Set(['pending', 'running']);

/**
 * One run, watched while it happens.
 *
 * Polled rather than pushed. A run reads a client's whole estate and takes minutes to hours,
 * and a socket held open for that is more than this is worth. What was worth fixing is what
 * the poll used to find: nothing, until a stage finished. A stage that reads an environment
 * takes minutes and the screen said "running" for all of them, so a slow export and a dead
 * worker looked the same, and the usual response to that is restarting the one that was
 * working.
 *
 * So two things changed. The worker says what it is on, every second or so, and the page
 * shows it. And the clock ticks here rather than only when an answer arrives, because a
 * number that moves is the difference between watching something work and watching
 * something that might have stopped.
 *
 * The stage list comes from the pipeline contract by way of the API, so a stage added to
 * analysis-stages.json appears here without anybody editing this file.
 */
export function RunProgress({ runId, onChanged }: { runId: string; onChanged?: () => void }) {
  const t = useT();
  const { culture } = useLanguage();
  const canRun = useCan('Contributor');
  const [detail, setDetail] = useState<RunDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const result = await getJson<RunDetail>(`/api/runs/${runId}`);
    if (result.data) setDetail(result.data);
    setError(result.error);
  }, [runId]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    // Only while something is actually moving. A finished run polled every five seconds for
    // the rest of the afternoon is a request a second from a room full of consultants, for
    // an answer that cannot change.
    if (!detail || !Moving.has(detail.run.status)) return undefined;

    // Two seconds while it moves. The note underneath a running stage changes about that
    // often, and asking for it more slowly than it changes is how a progress indicator ends
    // up feeling like a slideshow.
    const timer = window.setInterval(() => { void load(); }, 2000);
    return () => window.clearInterval(timer);
  }, [detail, load]);

  // A second hand. The elapsed time on a running stage is computed from its start, so it
  // only moved when a poll landed: every five seconds it jumped by five. This re-renders
  // between polls and nothing else.
  const [, tick] = useState(0);

  useEffect(() => {
    if (!detail || !Moving.has(detail.run.status)) return undefined;

    const timer = window.setInterval(() => tick((count) => count + 1), 1000);
    return () => window.clearInterval(timer);
  }, [detail]);

  if (!detail) {
    return <p className="dashboard-empty">{error ?? t('common.loading')}</p>;
  }

  const { run, stages } = detail;
  const progress = detail.stagesTotal === 0 ? 0 : (detail.stagesComplete / detail.stagesTotal) * 100;

  return (
    <div className="run-watch">
      <div className="run-watch-head">
        <span className="tag muted">
          {t('runs.stages-complete-of', detail.stagesComplete, detail.stagesTotal)}
        </span>
        <span className="run-watch-bar" aria-hidden="true"><span style={{ width: `${progress}%` }} /></span>
        <small>{summary(t, detail)}</small>
      </div>

      {error && <p className="curation-message danger">{error}</p>}

      {run.status === 'awaitingSelection' && (
        <SolutionPicker runId={runId} onChosen={() => { void load(); onChanged?.(); }} />
      )}

      <ol className="stage-timeline">
        {stages.map((stage) => (
          <StageRow
            key={stage.stageId}
            stage={stage}
            culture={culture}
            retry={canRun && stage.retryable && Rerunnable.has(stage.status) && !Moving.has(run.status)
              ? <StageRetry
                  runId={runId}
                  stageId={stage.stageId}
                  onChanged={() => { void load(); onChanged?.(); }}
                  discards={stages.filter((later) =>
                    later.order > stage.order && later.status !== 'pending').length} />
              : undefined} />
        ))}
      </ol>
    </div>
  );
}

/**
 * The stages worth offering to run again.
 *
 * A stage that never ran has nothing to redo. A stage that is running is already doing it.
 * Everything else is fair game, including a stage that succeeded: the commonest reason to
 * re-run an extraction is that somebody fixed a privilege, not that the extraction failed.
 */
const Rerunnable = new Set(['succeeded', 'partial', 'failed', 'cancelled', 'skipped']);

/** Where the run is, in the fewest words that are true. */
function summary(t: Translate, detail: RunDetail): string {
  switch (detail.run.status) {
    case 'pending': return t('runs.waiting-for-a-worker');
    case 'running': return t('runs.running-stage', detail.currentStageName ?? t('runs.the-pipeline'));
    case 'awaitingSelection': return t('runs.waiting-for-a-selection');
    case 'failed': return t('runs.stopped-at-stage', detail.currentStageName ?? t('runs.a-stage'));
    case 'partial': return t('runs.finished-with-gaps');
    case 'cancelled': return t('runs.cancelled');
    default: return t('runs.finished');
  }
}

function StageRow({ stage, culture, retry }: {
  stage: RunStage;
  culture: string;
  retry?: React.ReactNode;
}) {
  const t = useT();
  const [open, setOpen] = useState(false);
  const note = progressNote(t, stage);
  const share = progressShare(stage);

  // Half, unless the stage said otherwise. Most stages only learn their total by finishing,
  // and a half filled bar is honest about that; the ones that are working through a list of
  // solutions know exactly where they are and say so.
  const filled = stage.status === 'running' ? (share ?? 50)
    : stage.status === 'pending' ? 0
    : 100;

  return (
    <li className={`stage-row stage-${stage.status.toLowerCase()}`}>
      <button type="button" className="stage-head" onClick={() => setOpen(!open)} aria-expanded={open}>
        <span className="stage-marker" aria-hidden="true" />
        <span className="stage-title">
          <strong>{stage.name}</strong>
          <small>
            {elapsed(t, stage)}
            {stage.attempt > 1 ? ` · ${t('runs.attempt', stage.attempt)}` : ''}
          </small>
          {note && <small className="stage-note">{note}</small>}
        </span>
        <span className="stage-bar" aria-hidden="true"><span style={{ width: `${filled}%` }} /></span>
        <span className={`tag ${statusTag[stage.status] ?? 'muted'}`}>{t(`runs.status.${stage.status}`)}</span>
      </button>
      {open && (
        <div className="stage-body">
          <p>{stage.description}</p>
          <dl>
            <div><dt>{t('runs.started')}</dt><dd>{when(stage.startedUtc, culture, '—')}</dd></div>
            <div><dt>{t('runs.finished-at')}</dt><dd>{when(stage.completedUtc, culture, '—')}</dd></div>
            <div><dt>{t('runs.elapsed')}</dt><dd>{elapsed(t, stage) || '—'}</dd></div>
          </dl>
          {stage.error && <p className="stage-error">{stage.error}</p>}
          {retry && <div className="stage-actions">{retry}</div>}
        </div>
      )}
    </li>
  );
}

/**
 * What a running stage says it is doing, in the reader's language.
 *
 * The worker writes a key and its arguments rather than a sentence, because the sentence
 * belongs in whichever of the six languages somebody is reading and the worker has no idea
 * which that is. The arguments are the parts that are nobody's to translate: a solution's
 * unique name and a pair of counts.
 */
function progressNote(t: Translate, stage: RunStage): string | null {
  const parsed = parseNote(stage);
  if (!parsed) return null;

  return t(`runs.progress.${parsed.key}`, ...parsed.args);
}

/** How far through a stage that counts its work is, as a percentage, or null when it cannot say. */
function progressShare(stage: RunStage): number | null {
  const parsed = parseNote(stage);
  if (!parsed || parsed.args.length < 3) return null;

  const done = Number(parsed.args[1]);
  const total = Number(parsed.args[2]);

  if (!Number.isFinite(done) || !Number.isFinite(total) || total <= 0) return null;

  // Never full and never empty. A stage still has to write what it read, so a bar at a
  // hundred percent beside a spinner is a stage that looks stuck at the finish.
  return Math.min(95, Math.max(5, Math.round((done / total) * 100)));
}

/** The note as the worker wrote it, or null when there is none or it is not one of ours. */
function parseNote(stage: RunStage): { key: string; args: string[] } | null {
  if (stage.status !== 'running' || !stage.progress) return null;

  try {
    const parsed: unknown = JSON.parse(stage.progress);

    if (typeof parsed !== 'object' || parsed === null) return null;

    const { key, args } = parsed as { key?: unknown; args?: unknown };

    if (typeof key !== 'string' || key.length === 0) return null;

    return { key, args: Array.isArray(args) ? args.map(String) : [] };
  } catch {
    // A note that will not parse is a note nobody sees. It is the least important thing on
    // the page and it is not worth an error boundary.
    return null;
  }
}

/** How long a stage took, or has been taking. */
function elapsed(t: Translate, stage: RunStage): string {
  // A skipped stage was never going to run in this mode, so it has no duration. It was
  // showing the time since the run started, because the worker stamps a start on it.
  if (stage.status === 'skipped') return t('runs.status.skipped');

  if (!stage.startedUtc) return stage.status === 'pending' ? t('runs.not-started') : '';

  const finished = stage.completedUtc ? new Date(stage.completedUtc).getTime() : Date.now();
  const seconds = Math.round((finished - new Date(stage.startedUtc).getTime()) / 1000);

  if (seconds < 0) return '';
  if (seconds < 60) return t('runs.seconds', seconds);

  return t('runs.minutes', Math.round(seconds / 60));
}

/**
 * Running one stage again, and everything after it.
 *
 * The second half is the part worth saying out loud. A stage re-run on its own would write
 * over what the stages behind it had already consumed, so the run discards them too, and a
 * person who expected one stage to redo would otherwise watch an hour of work repeat with no
 * explanation on screen.
 */
function StageRetry({ runId, stageId, onChanged, discards }: {
  runId: string;
  stageId: string;
  onChanged: () => void;
  discards: number;
}) {
  const t = useT();
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function retry() {
    setBusy(true);
    const result = await sendJson(`/api/runs/${runId}/stages/${stageId}/retry`, 'POST');
    setError(result.error);
    setBusy(false);

    if (!result.error) {
      setConfirming(false);
      onChanged();
    }
  }

  if (confirming) {
    return (
      <span className="stage-confirm">
        <span>{t('runs.this-redoes-later-stages', discards)}</span>
        <button className="text-button" type="button" disabled={busy} onClick={() => void retry()}>
          {busy ? t('runs.queueing') : t('runs.yes-run-it-again')}
        </button>
        <button className="text-button" type="button" disabled={busy} onClick={() => setConfirming(false)}>
          {t('common.cancel')}
        </button>
        {error && <span className="stage-error">{error}</span>}
      </span>
    );
  }

  return (
    <>
      <button
        className="text-button"
        type="button"
        disabled={busy}
        onClick={() => (discards > 0 ? setConfirming(true) : void retry())}>
        {busy ? t('runs.queueing') : t('runs.retry-this-stage')}
      </button>
      {error && <span className="stage-error">{error}</span>}
    </>
  );
}

/**
 * What to read, before the run reads it.
 *
 * Most of what a Dataverse environment holds is Microsoft's own solutions. Reading them is
 * the longest part of a run and produces a report about Dynamics rather than about the work
 * the client paid somebody to do, so they start unticked and everything else starts ticked.
 *
 * Nothing is hidden. Every solution the run found is on the list with its publisher and its
 * size beside it, because the decision being made here is which parts of a client's estate
 * the report will and will not describe.
 */
function SolutionPicker({ runId, onChosen }: { runId: string; onChosen: () => void }) {
  const t = useT();
  const canRun = useCan('Contributor');
  const [choice, setChoice] = useState<Choice | null>(null);
  const [ticked, setTicked] = useState<Set<string>>(new Set());
  const [checks, setChecks] = useState(
    { solutionChecker: true, modelEstimates: true, environmentHealth: true, exportSolutions: true });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // The first real environment held 959 solutions. A list that long with no way to narrow
  // it is a list nobody reads: they tick the three they recognise at the top and continue.
  const [filter, setFilter] = useState('');
  const [hideMicrosoft, setHideMicrosoft] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void getJson<Choice>(`/api/runs/${runId}/solutions`).then((result) => {
      if (cancelled) return;
      setError(result.error);
      if (!result.data) return;

      setChoice(result.data);
      setTicked(new Set(result.data.solutions.filter((one) => one.selected).map((one) => one.uniqueName)));
      setChecks(result.data.checks);
    });

    return () => { cancelled = true; };
  }, [runId]);

  if (!choice) return <p className="dashboard-empty">{error ?? t('common.loading')}</p>;

  function toggle(uniqueName: string) {
    setTicked((current) => {
      const next = new Set(current);
      if (next.has(uniqueName)) next.delete(uniqueName); else next.add(uniqueName);
      return next;
    });
  }

  function only(firstParty: boolean) {
    setTicked(new Set(choice!.solutions.filter((one) => one.isFirstParty === firstParty).map((one) => one.uniqueName)));
  }

  async function confirm() {
    setBusy(true);
    const result = await sendJson(`/api/runs/${runId}/selection`, 'POST', {
      solutions: [...ticked],
      solutionChecker: checks.solutionChecker,
      modelEstimates: checks.modelEstimates,
      environmentHealth: checks.environmentHealth,
      exportSolutions: checks.exportSolutions
    });
    setError(result.error);
    setBusy(false);
    if (!result.error) onChosen();
  }

  const theirs = choice.solutions.filter((one) => !one.isFirstParty).length;

  const needle = filter.trim().toLowerCase();

  // Filtering changes what you can see, never what is ticked. Every bulk action below acts
  // on what is shown, which is the only behaviour that is not a trap: "select all" with a
  // filter on and a hidden selection would tick 959 things somebody could not see.
  const shown = choice.solutions.filter((one) => {
    if (hideMicrosoft && one.isFirstParty) return false;
    if (needle.length === 0) return true;

    return one.uniqueName.toLowerCase().includes(needle)
      || (one.friendlyName ?? '').toLowerCase().includes(needle)
      || (one.publisherName ?? '').toLowerCase().includes(needle);
  });

  return (
    <section className="solution-picker">
      <div className="solution-picker-head">
        <div>
          <strong>{t('runs.choose-what-to-read')}</strong>
          <p>{t('runs.found-solutions', choice.solutions.length, theirs)}</p>
        </div>
        <span className="solution-picker-actions">
          <button className="text-button" type="button" onClick={() => setTicked(new Set(shown.map((one) => one.uniqueName)))}>
            {t('runs.select-all')}
          </button>
          <button className="text-button" type="button" onClick={() => only(false)}>
            {t('runs.select-theirs')}
          </button>
          <button className="text-button" type="button" onClick={() => setTicked(new Set())}>
            {t('runs.select-none')}
          </button>
        </span>
      </div>

      <div className="solution-filter">
        <input
          type="search"
          value={filter}
          placeholder={t('runs.filter-solutions')}
          onChange={(event) => setFilter(event.target.value)} />

        <label className="solution-filter-toggle">
          <input
            type="checkbox"
            checked={hideMicrosoft}
            onChange={(event) => setHideMicrosoft(event.target.checked)} />
          <span>{t('runs.hide-microsoft')}</span>
        </label>

        <span className="hint">{t('runs.showing-of', shown.length, choice.solutions.length, ticked.size)}</span>
      </div>

      <ul className="solution-list">
        {shown.map((solution) => (
          <li key={solution.uniqueName} className={`solution-item ${solution.isFirstParty ? 'first-party' : ''}`}>
            <label className="solution-label">
              <input
                type="checkbox"
                checked={ticked.has(solution.uniqueName)}
                onChange={() => toggle(solution.uniqueName)} />
              <span className="solution-name">
                <strong>{solution.friendlyName || solution.uniqueName}</strong>
                <small>
                  {solution.uniqueName}
                  {solution.publisherName ? ` · ${solution.publisherName}` : ''}
                  {solution.version ? ` · ${solution.version}` : ''}
                </small>
              </span>
            </label>
            <span className="solution-marks">
              {solution.componentCount !== null && (
                <span className="tag muted">{t('runs.components', solution.componentCount)}</span>
              )}
              {solution.isManaged && <span className="tag muted">{t('runs.managed')}</span>}
              {solution.isFirstParty && <span className="tag muted">{t('runs.microsoft')}</span>}
            </span>
          </li>
        ))}
      </ul>

      <div className="solution-checks">
        <strong>{t('runs.also-run')}</strong>
        {/* First in the list because it is what the two below it read. Seventeen rules need
            the solution file and none of them can run without this. */}
        <label className="solution-check">
          <input
            type="checkbox"
            checked={checks.exportSolutions}
            onChange={(event) => setChecks({ ...checks, exportSolutions: event.target.checked })} />
          <span><strong>{t('runs.check.exportSolutions')}</strong><small>{t('runs.check.exportSolutions-note')}</small></span>
        </label>
        <label className="solution-check">
          <input
            type="checkbox"
            checked={checks.solutionChecker}
            onChange={(event) => setChecks({ ...checks, solutionChecker: event.target.checked })} />
          <span>
            <strong>{t('runs.check.solutionChecker')}</strong>
            <small>{t('runs.check.solutionChecker-note')}</small>

            {/* Said here rather than discovered later. The checker refuses without a
                geography, deliberately, because where a client's solution is uploaded for
                analysis is a data residency decision. On the run this was written for the
                extraction took eight minutes and the checker refused in one second. */}
            {checks.solutionChecker && !choice.checkerGeography && (
              <small className="solution-check-warning">{t('runs.check.no-geography')}</small>
            )}
          </span>
        </label>
        <label className="solution-check">
          <input
            type="checkbox"
            checked={checks.modelEstimates}
            onChange={(event) => setChecks({ ...checks, modelEstimates: event.target.checked })} />
          <span><strong>{t('runs.check.modelEstimates')}</strong><small>{t('runs.check.modelEstimates-note')}</small></span>
        </label>
        <label className="solution-check">
          <input
            type="checkbox"
            checked={checks.environmentHealth}
            onChange={(event) => setChecks({ ...checks, environmentHealth: event.target.checked })} />
          <span><strong>{t('runs.check.environmentHealth')}</strong><small>{t('runs.check.environmentHealth-note')}</small></span>
        </label>
      </div>

      {error && <p className="curation-message danger">{error}</p>}

      {/* Said before the button rather than after it. Choosing nothing is a legitimate answer
          and it produces a report about an estate nobody read, which is the one thing this
          product must never let somebody publish by accident. */}
      {ticked.size === 0 && <p className="panel-note">{t('runs.nothing-chosen-warning')}</p>}

      <div className="solution-picker-foot">
        <button
          className="primary-button"
          type="button"
          disabled={busy || !canRun}
          onClick={() => void confirm()}>
          {busy ? t('runs.starting') : t('runs.continue-the-run', ticked.size)}
        </button>
      </div>
    </section>
  );
}
