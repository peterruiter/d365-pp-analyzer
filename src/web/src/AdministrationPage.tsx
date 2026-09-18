import { useEffect, useState } from 'react';
import { useT, useLanguage } from './i18n';
import { UserAdministration } from './UserAdministration';
import { AccessWizard } from './AccessWizard';
import { getJson, sendJson, when, type Engagement } from './workspace';

/**
 * Engagements, the people who may see them, and who may use the product at all.
 *
 * Two sections behind a tab bar, the same shape the Intent Miner uses, because a consultant
 * has usually already used the other product and the two should not need learning twice.
 * The users tab is only there for a global administrator: everybody else cannot change any
 * of it, and a tab that shows a list of people you may not act on is a tab that raises a
 * question with no answer.
 *
 * Also the screen a reader with no engagement lands on, which is why it has to work with
 * nothing selected. A consultant opening the product for the first time meets this page,
 * and an empty product that says nothing about what to do next is one they close.
 */
export function AdministrationPage({
  mode, onModeChange, engagements, setEngagements, currentEngagement, onSwitch, isGlobalAdmin, currentUserId
}: {
  mode: 'list' | 'create';
  onModeChange: (mode: 'list' | 'create') => void;
  engagements: Engagement[];
  setEngagements: (engagements: Engagement[]) => void;
  currentEngagement: Engagement | null;
  onSwitch: (engagement: Engagement | null) => void;
  isGlobalAdmin: boolean;
  currentUserId: string;
}) {
  const t = useT();
  const { culture } = useLanguage();
  const [section, setSection] = useState<'engagements' | 'users'>('engagements');
  const [name, setName] = useState('');
  const [clientName, setClientName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [accessEngagement, setAccessEngagement] = useState<Engagement | null>(null);

  useEffect(() => {
    // Clearing the form when the reader leaves it, so reopening does not present a
    // half filled engagement somebody abandoned.
    if (mode === 'list') { setName(''); setClientName(''); setError(null); }
  }, [mode]);

  useEffect(() => {
    // Losing the global role while the users tab is open would otherwise leave somebody
    // looking at a list they can no longer load.
    if (!isGlobalAdmin) setSection('engagements');
  }, [isGlobalAdmin]);

  async function refresh() {
    const result = await getJson<Engagement[]>('/api/engagements');
    if (result.data) setEngagements(result.data);
  }

  async function create() {
    if (!name.trim()) return;
    setBusy(true);

    const result = await sendJson<Engagement>('/api/engagements', 'POST', {
      name: name.trim(),
      clientName: clientName.trim() || null
    });

    if (result.error) {
      setError(result.error);
    } else {
      await refresh();
      onModeChange('list');
      if (result.data) onSwitch(result.data);
    }

    setBusy(false);
  }

  async function remove(engagementId: string) {
    setBusy(true);
    const result = await sendJson(`/api/engagements/${engagementId}`, 'DELETE');

    if (result.error) {
      setError(result.error);
    } else {
      await refresh();
      if (currentEngagement?.engagementId === engagementId) onSwitch(null);
    }

    setConfirming(null);
    setBusy(false);
  }

  if (mode === 'create') {
    return (
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('admin.engagements')}</p>
            <h2>{t('admin.new-engagement')}</h2>
          </div>
        </div>

        <div className="wizard-body">
          <label className="field-label">
            {t('admin.engagement-name')}
            <input type="text" value={name} onChange={(event) => setName(event.target.value)} />
          </label>

          <label className="field-label">
            {t('admin.client-name')}
            <input type="text" value={clientName} onChange={(event) => setClientName(event.target.value)} />
          </label>

          {error && <p className="curation-message danger">{error}</p>}

          <div className="wizard-foot">
            <button type="button" className="secondary-button" onClick={() => onModeChange('list')}>
              {t('admin.cancel')}
            </button>
            <button type="button" className="primary-button" disabled={busy || !name.trim()} onClick={() => void create()}>
              {t('admin.create')}
            </button>
          </div>
        </div>
      </section>
    );
  }

  return (
    <>
      <nav className="detail-tabs" aria-label={t('admin.sections')}>
        <button
          type="button"
          className={section === 'engagements' ? 'active' : ''}
          onClick={() => setSection('engagements')}
        >
          {t('admin.engagements')}
        </button>
        {isGlobalAdmin && (
          <button
            type="button"
            className={section === 'users' ? 'active' : ''}
            onClick={() => setSection('users')}
          >
            {t('admin.users')}
          </button>
        )}
      </nav>

      {section === 'users' && isGlobalAdmin ? (
        <UserAdministration currentUserId={currentUserId} />
      ) : (
        <section className="panel">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{t('view.administration')}</p>
              <h2>{t('admin.engagements')}</h2>
              <p className="panel-note standalone">{t('admin.engagements-lede')}</p>
            </div>
            <button type="button" className="primary-button" onClick={() => onModeChange('create')}>
              {t('admin.new-engagement')}
            </button>
          </div>

          {engagements.length === 0 ? (
            <p className="dashboard-empty">{t('common.nothing-here-yet')}</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{t('admin.engagement-name')}</th>
                    <th scope="col">{t('admin.client-name')}</th>
                    <th scope="col">{t('admin.your-role')}</th>
                    <th scope="col" />
                  </tr>
                </thead>
                <tbody>
                  {engagements.map((engagement) => (
                    <tr key={engagement.engagementId}>
                      <th scope="row">
                        <button type="button" className="text-button" onClick={() => onSwitch(engagement)}>
                          {engagement.name}
                        </button>
                        <span className="hint">{when(engagement.createdUtc, culture, t('common.never'))}</span>
                      </th>
                      <td className="wrapping-cell">{engagement.clientName ?? ''}</td>
                      <td><span className="tag muted">{t('role.' + engagement.accessRole.toLowerCase())}</span></td>
                      <td>
                        <div className="connection-actions">
                          {/* Only somebody who can change who is on an engagement is offered
                              the chance. A Contributor seeing a disabled Access button learns
                              nothing except that the product has one. */}
                          {(isGlobalAdmin || engagement.accessRole === 'Admin') && (
                            <button
                              type="button"
                              className="secondary-button small"
                              onClick={() => setAccessEngagement(engagement)}
                            >
                              {t('admin.access')}
                            </button>
                          )}

                          {/* Deleting an engagement takes everything a client gave us with it,
                              so it asks twice. */}
                          {(isGlobalAdmin || engagement.accessRole === 'Admin') && (
                            confirming === engagement.engagementId ? (
                              <>
                                <button
                                  type="button"
                                  className="danger-button small"
                                  disabled={busy}
                                  onClick={() => void remove(engagement.engagementId)}
                                >
                                  {t('admin.delete-engagement')}
                                </button>
                                <button type="button" className="secondary-button small" onClick={() => setConfirming(null)}>
                                  {t('admin.cancel')}
                                </button>
                              </>
                            ) : (
                              <button
                                type="button"
                                className="danger-button small outline"
                                onClick={() => setConfirming(engagement.engagementId)}
                              >
                                {t('admin.remove')}
                              </button>
                            )
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {confirming && <p className="panel-note">{t('admin.delete-engagement-warning')}</p>}
          {error && <p className="curation-message danger">{error}</p>}
        </section>
      )}

      {accessEngagement && (
        <AccessWizard
          engagement={accessEngagement}
          currentUserId={currentUserId}
          onClose={() => setAccessEngagement(null)}
        />
      )}
    </>
  );
}
