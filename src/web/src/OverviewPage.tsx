import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { severityTag } from './tags';
import { getJson, range, type Assessment } from './workspace';

/** One step in taking an engagement from nothing to a migrated environment. */
type Step = {
  id: string;
  workspace: string;
  state: 'blocked' | 'current' | 'done';
};

/**
 * Where an engagement stands, and the next thing to do.
 *
 * The checklist is the point of this page. A consultant opening the product for the first
 * time should not have to work out the order things happen in, and the steps are derived
 * from what actually exists rather than stored, so there is no progress column anybody can
 * forget to update and nothing to repair when a run is deleted.
 */
export function OverviewPage({ engagementId, onNavigate }: {
  engagementId: string;
  onNavigate: (workspace: string) => void;
}) {
  const t = useT();
  const [steps, setSteps] = useState<Step[] | null>(null);
  const [assessment, setAssessment] = useState<Assessment | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const [stepsResult, assessmentResult] = await Promise.all([
        getJson<Step[]>(`/api/engagements/${engagementId}/next-steps`),
        getJson<Assessment>(`/api/engagements/${engagementId}/assessment`)
      ]);

      if (cancelled) return;
      setSteps(stepsResult.data ?? []);
      setAssessment(assessmentResult.data);
      setError(stepsResult.error);
    }

    void load();
    return () => { cancelled = true; };
  }, [engagementId]);

  if (error) {
    return (
      <section className="panel">
        <div className="panel-heading"><div><h2>{t('common.something-went-wrong')}</h2></div></div>
        <p className="dashboard-empty">{error}</p>
      </section>
    );
  }

  if (!steps) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  const done = steps.filter((step) => step.state === 'done').length;
  const discovered = assessment !== null && assessment.runId !== null;

  return (
    <>
      <section className="checklist">
        <div className="checklist-head">
          <h2>{t('view.overview')}</h2>
          <span className="checklist-progress">{done}/{steps.length}</span>
        </div>

        <ol className="checklist-steps">
          {steps.map((step, index) => (
            <li key={step.id} className={`checklist-step checklist-${step.state}`}>
              <span className="checklist-marker" aria-hidden="true">
                {step.state === 'done' ? '✓' : index + 1}
              </span>

              <div className="checklist-text">
                <span className="checklist-name">{t('step.' + step.id)}</span>
                <p className="checklist-guidance">{t('step.' + step.state)}</p>
              </div>

              {step.state === 'current' && (
                <button type="button" className="primary-button" onClick={() => onNavigate(step.workspace)}>
                  {t('overview.go-there')} <span aria-hidden="true">&rarr;</span>
                </button>
              )}
            </li>
          ))}
        </ol>
      </section>

      {!discovered ? (
        <section className="panel">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{t('view.overview')}</p>
              <h2>{t('overview.nothing-discovered-yet')}</h2>
            </div>
            <button type="button" className="primary-button" onClick={() => onNavigate('Runs')}>
              {t('overview.run-a-discovery')}
            </button>
          </div>
          <p className="panel-note">{t('overview.nothing-discovered-lede')}</p>
        </section>
      ) : (
        <>
          <section className="metrics-grid" aria-label={t('view.overview')}>
            <article className="metric-card metric-primary">
              <span className="metric-label">{t('overview.records-discovered')}</span>
              <strong>{assessment!.componentCount}</strong>
            </article>
            <article className="metric-card">
              <span className="metric-label">{t('overview.entities-covered')}</span>
              <strong>{assessment!.componentTypeCount}</strong>
            </article>
            <article className="metric-card">
              {/* Two numbers, never one. A tool that scanned an estate it has never seen does
                  not get to quote a single figure. */}
              <span className="metric-label">{t('overview.effort-estimate')}</span>
              <strong>{range(assessment!.totalLowHours, assessment!.totalHighHours)}</strong>
              <span className="metric-note">{t('overview.hours')}</span>
            </article>
            <article className="metric-card">
              <span className="metric-label">{t('overview.risks-raised')}</span>
              <strong>{assessment!.findingCount}</strong>
            </article>
          </section>

          {assessment!.topFindings.length > 0 && (
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <p className="eyebrow">{t('view.overview')}</p>
                  <h2>{t('overview.risks-raised')}</h2>
                </div>
              </div>

              <div className="wizard-body">
                <ol className="check-list">
                  {assessment!.topFindings.map((risk) => (
                    <li key={risk.id} className="check-warning">
                      <span className="check-mark" aria-hidden="true" />
                      <div>
                        <strong>{risk.title}</strong>
                        <p>{risk.where}</p>
                      </div>
                      <span className={`tag ${severityTag[risk.severity.toLowerCase()] ?? 'muted'}`}>
                        {t('severity.' + risk.severity.toLowerCase())}
                      </span>
                    </li>
                  ))}
                </ol>
              </div>
            </section>
          )}

          <section className="panel">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">{t('view.findings')}</p>
                <h2>{t('overview.open-the-findings')}</h2>
              </div>
              <button type="button" className="primary-button" onClick={() => onNavigate('Findings')}>
                {t('overview.open-the-findings')}
              </button>
            </div>

            {/*
              The caveat travels with the invitation to read the findings, not after it. A
              short list of findings and a long list of checks that could not run are the same
              screen, and somebody who reads only the first has been misled by the second.
            */}
            <p className="panel-note">
              {t('findings.not-assessed-title', String(assessment!.notAssessedCount), String(assessment!.ruleCount))}
            </p>
          </section>
        </>
      )}
    </>
  );
}
