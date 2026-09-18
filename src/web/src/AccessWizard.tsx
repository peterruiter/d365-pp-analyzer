import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { getJson, sendJson, type Engagement } from './workspace';

/** Somebody already on this engagement. */
type Member = {
  userId: string;
  displayName: string;
  email: string | null;
  role: string;
};

/** Somebody admitted to the product who is not on it yet. */
type Candidate = {
  userId: string;
  displayName: string;
  email: string | null;
  isGlobalAdmin: boolean;
};

/**
 * The three levels, strongest last.
 *
 * The value sent to the API is the canonical English role and the label is translated, which
 * is deliberately not how the Intent Miner does it. There the option carries no value
 * attribute, so the submitted role is whatever the option's text happens to be, and on a
 * Dutch or German interface that is a word the server has never heard of. Copying the layout
 * is the point of this screen; copying that is not.
 */
const roles = ['Viewer', 'Contributor', 'Admin'] as const;

/**
 * Who may see one engagement, and what they may do there.
 *
 * A modal rather than a page, because it is always opened from a row in a list and closing it
 * should put the reader back where they were rather than somewhere they have to navigate out
 * of again.
 */
export function AccessWizard({
  engagement, currentUserId, onClose
}: {
  engagement: Engagement;
  currentUserId: string;
  onClose: () => void;
}) {
  const t = useT();
  const [members, setMembers] = useState<Member[]>([]);
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [selected, setSelected] = useState('');
  const [role, setRole] = useState<string>('Contributor');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    const [access, available] = await Promise.all([
      getJson<Member[]>(`/api/engagements/${engagement.engagementId}/access`),
      getJson<Candidate[]>(`/api/engagements/${engagement.engagementId}/available-users`)
    ]);

    if (access.error || available.error) {
      setError(access.error ?? available.error);
      return;
    }

    setMembers(access.data ?? []);
    setCandidates(available.data ?? []);
  }

  useEffect(() => {
    void load();
    // Reloading when the engagement changes rather than only on mount, because the same
    // component instance is reused when a reader opens access on a second row.
  }, [engagement.engagementId]);

  // Escape closes it, which is what a dialog is expected to do and what somebody reaches for
  // before they look for the button.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  async function grant() {
    if (!selected) return;
    setBusy(true);
    setError(null);

    const result = await sendJson(`/api/engagements/${engagement.engagementId}/access`, 'PUT', {
      upn: selected,
      role
    });

    if (result.error) setError(result.error);
    else { setSelected(''); await load(); }

    setBusy(false);
  }

  async function revoke(userId: string) {
    setBusy(true);
    setError(null);

    const result = await sendJson(`/api/engagements/${engagement.engagementId}/access/${encodeURIComponent(userId)}`, 'DELETE');

    if (result.error) setError(result.error);
    else await load();

    setBusy(false);
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="forecast-modal" role="dialog" aria-modal="true" aria-labelledby="access-title">
        <div className="modal-header">
          <div>
            <p className="eyebrow">{t('admin.engagement-access')}</p>
            <h2 id="access-title">{engagement.name}</h2>
          </div>
          <button className="icon-button" type="button" onClick={onClose} aria-label={t('admin.close-access')}>
            &times;
          </button>
        </div>

        <div className="wizard-body">
          <p className="wizard-help">{t('admin.access-help')}</p>

          <div className="access-form">
            <label className="field-label">
              {t('admin.admitted-user')}
              <select value={selected} onChange={(event) => setSelected(event.target.value)}>
                <option value="">{t('admin.select-a-user')}</option>
                {candidates.map((candidate) => (
                  <option key={candidate.userId} value={candidate.userId}>
                    {candidate.displayName} ({candidate.email ?? candidate.userId})
                    {candidate.isGlobalAdmin ? ` · ${t('admin.global-admin')}` : ''}
                  </option>
                ))}
              </select>
            </label>

            <label className="field-label">
              {t('admin.role')}
              <select value={role} onChange={(event) => setRole(event.target.value)}>
                {roles.map((known) => (
                  <option key={known} value={known}>{t('role.' + known.toLowerCase())}</option>
                ))}
              </select>
            </label>

            <button
              type="button"
              className="primary-button"
              disabled={busy || !selected}
              onClick={() => void grant()}
            >
              {t('admin.grant-access')}
            </button>
          </div>

          {candidates.length === 0 && (
            <p className="panel-note">{t('admin.everybody-already-here')}</p>
          )}

          {error && <div className="wizard-callout warning"><strong>{error}</strong></div>}

          <div className="access-list">
            {members.length === 0 ? (
              <p className="dashboard-empty">{t('admin.nobody-on-this-engagement')}</p>
            ) : members.map((member) => (
              <div className="access-row" key={member.userId}>
                <span>
                  <strong>{member.displayName || member.email || member.userId}</strong>
                  <small>{(member.email ?? member.userId)} &middot; {t('role.' + member.role.toLowerCase())}</small>
                </span>
                {/* Removing your own last way in is how somebody locks themselves out of a
                    client's engagement on a Friday. A global administrator can put it back,
                    but only if there is one who is not you. */}
                <button
                  type="button"
                  className="danger-button small outline"
                  disabled={busy || member.userId === currentUserId}
                  onClick={() => void revoke(member.userId)}
                >
                  {t('admin.remove')}
                </button>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
