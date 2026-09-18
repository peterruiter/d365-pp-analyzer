import { useCallback, useEffect, useState } from 'react';
import { getJson } from './workspace';
import { useT, type Translate } from './i18n';

type State = 'Ok' | 'Degraded' | 'Failed' | 'NotConfigured';

type Check = {
  id: string;
  name: string;
  group: string;
  state: State;
  detail: string;
  remediation: string | null;
  command: string | null;
  canRepair: boolean;
};

type Health = { checks: Check[]; checkedUtc: string };

/** The order the groups read in: what must work, then what it reads, writes, runs and produces. */
const groups: { id: string; labelKey: string }[] = [
  { id: 'platform', labelKey: 'health.group-platform' },
  { id: 'sources', labelKey: 'health.group-sources' },
  { id: 'target', labelKey: 'health.group-target' },
  { id: 'pipeline', labelKey: 'health.group-pipeline' },
  { id: 'deliverables', labelKey: 'health.group-deliverables' }
];

/**
 * Whether this deployment is actually wired up, and what to do about whatever is not.
 *
 * Ordered worst first. A page listing twelve green checks with the one red one buried in the
 * middle is a page that gets skimmed, and the only reason to open this is that something is
 * wrong. Every failing check carries either a command to run or a button that fixes it.
 */
export function SystemHealthPage({ isGlobalAdmin }: { isGlobalAdmin: boolean }) {
  const t = useT();
  const [health, setHealth] = useState<Health | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!isGlobalAdmin) return;

    setError('');
    getJson<Health>('/api/system/health').then((result) => {
      if (result.data) setHealth(result.data);
      else setError(result.error ?? t('health.could-not-read'));
    });
  }, [t, isGlobalAdmin]);

  useEffect(load, [load]);

  async function repair(id: string) {
    setBusy(id);

    try {
      const response = await fetch(`/api/system/health/${id}/repair`, { method: 'POST' });
      if (!response.ok) throw new Error('repair failed');

      const updated = await response.json() as Check;

      setHealth((current) => current === null
        ? current
        : { ...current, checks: current.checks.map((check) => check.id === id ? updated : check) });
    } catch {
      setError(t('health.repair-did-not-work'));
    } finally {
      setBusy(null);
    }
  }

  if (!isGlobalAdmin) {
    // The detail names servers, identities and what they are refused, which is a map of the
    // deployment for anybody who should not have one.
    return (
      <div className="wizard-callout">
        <strong>{t('health.only-a-global-administrator')}</strong>
        <span>{t('health.only-a-global-administrator-detail')}</span>
      </div>
    );
  }

  if (error !== '' && health === null) {
    return <div className="wizard-callout warning"><strong>{error}</strong></div>;
  }

  if (health === null) {
    return <p className="dashboard-empty">{t('health.checking')}</p>;
  }

  const failing = health.checks.filter((check) => check.state === 'Failed').length;
  const degraded = health.checks.filter((check) => check.state === 'Degraded').length;

  return (
    <>
      <div className="panel-heading">
        <div>
          <p className="eyebrow">{t('health.system')}</p>
          <h2>{summary(failing, degraded, t)}</h2>
        </div>
        <div className="wizard-actions">
          <button className="secondary-button" type="button" onClick={load}>{t('health.check-again')}</button>
        </div>
      </div>

      <p className="panel-note">{t('health.every-check-does-the-thing')}</p>

      {error !== '' && <div className="wizard-callout warning"><strong>{error}</strong></div>}

      {groups.map((group) => {
        const checks = health.checks
          .filter((check) => check.group === group.id)
          .sort((a, b) => weight(a.state) - weight(b.state));

        if (checks.length === 0) return null;

        return (
          <section className="detail-panel" key={group.id}>
            <div className="panel-heading"><div><h3>{t(group.labelKey)}</h3></div></div>

            <div className="admin-list">
              {checks.map((check) => (
                <article className={`admin-row health-row ${check.state.toLowerCase()}`} key={check.id}>
                  <div>
                    <strong>
                      <span className={`tag ${tagClass(check.state)}`}>
                        {t(`health.state-${check.state.toLowerCase()}`)}
                      </span>
                      {' '}{check.name}
                    </strong>
                    <span>{check.detail}</span>
                    {check.remediation !== null && <small>{check.remediation}</small>}
                    {check.command !== null && <pre className="config-json">{check.command}</pre>}
                  </div>

                  {check.canRepair && (
                    <div className="admin-actions">
                      <button
                        className="secondary-button"
                        type="button"
                        disabled={busy === check.id}
                        onClick={() => void repair(check.id)}
                      >
                        {busy === check.id ? t('health.repairing') : t('health.repair-this')}
                      </button>
                    </div>
                  )}
                </article>
              ))}
            </div>
          </section>
        );
      })}

      <SyncfusionSetting onSaved={load} />
    </>
  );
}

/**
 * Replacing the PDF licence key from the page that reports it missing.
 *
 * The key goes to Key Vault, which is where a secret this company paid for belongs, and is
 * registered immediately so the next report renders without waiting for a restart.
 *
 * Checked before it is stored. A key issued for the UI components alone validates and then
 * watermarks every page, and finding that out when a client opens the report is too late.
 */
function SyncfusionSetting({ onSaved }: { onSaved: () => void }) {
  const t = useT();
  const [key, setKey] = useState('');
  const [state, setState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [note, setNote] = useState('');

  async function save() {
    setState('saving');

    try {
      const response = await fetch('/api/system/settings/syncfusion', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key })
      });

      if (!response.ok) throw new Error('save failed');

      const result = await response.json() as { stored: boolean; coversPdf: boolean; note: string };

      setNote(result.note + (result.coversPdf ? '' : ' ' + t('health.this-key-does-not-cover-pdf')));
      setState('saved');
      setKey('');
      onSaved();
    } catch {
      setState('error');
    }
  }

  return (
    <section className="detail-panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">{t('health.settings')}</p>
          <h3>{t('health.pdf-licence-key')}</h3>
        </div>
      </div>

      <p className="panel-note">{t('health.the-key-is-stored-in-key-vault')}</p>

      <div className="access-form">
        <label className="field-label">
          {t('health.replacement-key')}
          <input
            type="password"
            value={key}
            autoComplete="off"
            onChange={(event) => setKey(event.target.value)}
            placeholder={t('health.paste-the-key-from-syncfusion')}
          />
        </label>

        <button
          className="primary-button"
          type="button"
          disabled={key.trim() === '' || state === 'saving'}
          onClick={() => void save()}
        >
          {state === 'saving' ? t('health.saving') : t('health.save-the-key')}
        </button>
      </div>

      {state === 'saved' && (
        <div className="wizard-callout"><strong>{t('health.key-replaced')}</strong><span>{note}</span></div>
      )}
      {state === 'error' && (
        <div className="wizard-callout warning"><strong>{t('health.the-key-was-not-accepted')}</strong></div>
      )}
    </section>
  );
}

/** Worst first. The reason anybody opened this page is the thing that is wrong. */
function weight(state: State): number {
  return { Failed: 0, Degraded: 1, NotConfigured: 2, Ok: 3 }[state];
}

function tagClass(state: State): string {
  return { Failed: 'danger', Degraded: 'warning', NotConfigured: 'muted', Ok: 'complete' }[state];
}

function summary(failing: number, degraded: number, t: Translate): string {
  // One is a different sentence, not the same sentence with a 1 in it. "1 parts are not
  // working" is the kind of thing that makes a product look unfinished at exactly the moment
  // somebody is already annoyed with it.
  if (failing === 1) return t('health.one-part-is-not-working');
  if (failing > 1) return t('health.n-parts-are-not-working', failing);
  if (degraded === 1) return t('health.one-part-needs-attention');
  if (degraded > 1) return t('health.n-parts-need-attention', degraded);
  return t('health.everything-is-working');
}
