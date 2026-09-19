import { useEffect, useState } from 'react';
import { useT, type Translate } from './i18n';
import { getJson, sendJson } from './workspace';

/** The axes, as the report model groups them. */
type Axes = {
  scale: string;
  groups: { id: string; axes: string[] }[];
};

/** One axis, as somebody scored it. */
type Score = {
  axisId: string;
  score: number;
  evidence: string | null;
  updatedByName: string;
};

/** The scale, as buttons rather than a slider. Six values, and a slider hides which one is set. */
const points = [0, 1, 2, 3, 4, 5];

/**
 * One capability, its score and who said so.
 *
 * The score is a row of buttons rather than a number box or a slider. There are six values
 * on this scale, a number box invites a 7 and a decimal, and a slider is a control somebody
 * has to drag precisely to say something they already know exactly.
 */
function Axis({ axis, existing, engagementId, readOnly, t, onScored }: {
  axis: string;
  existing: Score | undefined;
  engagementId: string;
  readOnly: boolean;
  t: Translate;
  onScored: (axisId: string, score: number | null, evidence: string | null) => void;
}) {
  const [evidence, setEvidence] = useState(existing?.evidence ?? '');
  const [busy, setBusy] = useState(false);

  useEffect(() => { setEvidence(existing?.evidence ?? ''); }, [existing?.evidence]);

  async function save(score: number | null, note: string | null) {
    if (readOnly) return;

    setBusy(true);

    const result = await sendJson(
      `/api/engagements/${engagementId}/maturity/${axis}`, 'PUT', { score, evidence: note });

    if (!result.error) onScored(axis, score, note);

    setBusy(false);
  }

  return (
    <div className="axis-row">
      <span className="axis-name">{t('axis.' + axis)}</span>

      <span className="axis-scale" role="group" aria-label={t('axis.' + axis)}>
        {points.map((point) => (
          <button
            key={point}
            type="button"
            disabled={readOnly || busy}
            aria-pressed={existing?.score === point}
            className={`axis-point ${existing?.score === point ? 'chosen' : ''}`}
            // Pressing the score it already has takes it away. Nought is a real score on
            // this scale and means the capability is absent; not having looked is a
            // different statement and needs a way back to it.
            onClick={() => void save(existing?.score === point ? null : point, evidence || null)}
          >
            {point}
          </button>
        ))}
      </span>

      <input
        type="text"
        className="axis-evidence"
        value={evidence}
        readOnly={readOnly}
        placeholder={t('maturity.who-told-you')}
        onChange={(event) => setEvidence(event.target.value)}
        onBlur={() => {
          if ((existing?.evidence ?? '') !== evidence && existing?.score !== undefined) {
            void save(existing.score, evidence || null);
          }
        }}
      />
    </div>
  );
}

/**
 * The functional maturity scores.
 *
 * Sixteen capability axes in four groups, scored nought to five from interviews and
 * demonstrations. No metadata anywhere says whether campaign management is any good, so the
 * product supplies the axes, the form and the chart, and none of the numbers.
 *
 * The axes come from report-model.json, so the form, the radar in the report and the stored
 * scores cannot disagree about which axes exist or what order they go round in. A radar
 * whose axes move between two engagements is not comparable with itself.
 */
export function MaturityPanel({ engagementId, readOnly }: { engagementId: string; readOnly: boolean }) {
  const t = useT();
  const [axes, setAxes] = useState<Axes | null>(null);
  const [scores, setScores] = useState<Score[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const [axisResult, scoreResult] = await Promise.all([
        getJson<Axes>('/api/maturity-axes'),
        getJson<Score[]>(`/api/engagements/${engagementId}/maturity`)
      ]);

      if (cancelled) return;

      setAxes(axisResult.data);
      setScores(scoreResult.data ?? []);
      setError(axisResult.error ?? scoreResult.error);
    }

    void load();
    return () => { cancelled = true; };
  }, [engagementId]);

  if (error) return <p className="error">{error}</p>;
  if (!axes) return null;

  function remember(axisId: string, score: number | null, evidence: string | null) {
    setScores((current) => {
      const rest = current.filter((entry) => entry.axisId !== axisId);

      if (score === null) return rest;

      return [...rest, { axisId, score, evidence, updatedByName: '' }];
    });
  }

  const total = axes.groups.reduce((count, group) => count + group.axes.length, 0);

  return (
    <section className="panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">{t('maturity.eyebrow')}</p>
          <h2>{t('maturity.heading')}</h2>
        </div>
        <span className="tag muted">{t('maturity.n-of-m', scores.length, total)}</span>
      </div>

      <div className="maturity-body">
        <p className="narrative-prompt">{t('maturity.prompt')}</p>
        <p className="hint">{axes.scale}</p>

        {axes.groups.map((group) => (
          <div className="axis-group" key={group.id}>
            <h3>{t('axis.group.' + group.id)}</h3>

            {group.axes.map((axis) => (
              <Axis
                key={axis}
                axis={axis}
                existing={scores.find((entry) => entry.axisId === axis)}
                engagementId={engagementId}
                readOnly={readOnly}
                t={t}
                onScored={remember}
              />
            ))}
          </div>
        ))}

        {/*
          Said on the screen as well as on the chart. This is the section of the report most
          likely to be quoted back, and a score with no provenance is the easiest number in
          it to have made up.
        */}
        <p className="panel-note">{t('maturity.unscored-are-left-out')}</p>
      </div>
    </section>
  );
}
