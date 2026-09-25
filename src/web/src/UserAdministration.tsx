import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { getJson, sendJson, type AdmittedUser } from './workspace';

/**
 * Who may use the product.
 *
 * Signing in and being admitted are separate. Anybody in the tenant can complete the first;
 * only somebody listed here can do the second. A tool that reads a client's whole contact
 * centre configuration is not one that everybody with a mailbox should be able to open, and
 * this screen is where that line is drawn.
 *
 * Being admitted is also not the same as being given work. Somebody admitted and put on no
 * engagement can read the demonstration estate and nothing else, which is what the third line
 * of their row says when they have no grants.
 *
 * Only a global administrator sees it, because only a global administrator can change it.
 *
 * Laid out as Intent Miner lays it out, down to the class names: the form above, the people
 * below, one row each. A consultant has usually used the other product first, and the two
 * screens doing the same job in different shapes is a support conversation every time. Two
 * things are deliberately not copied. The buttons are pills rather than text links, because
 * that is this product's button and a row of underlined words is not one. And removing
 * somebody asks in the row rather than through the browser's confirm dialog, which cannot be
 * translated and appears somewhere the reader is not looking.
 */
export function UserAdministration({ currentUserId }: { currentUserId: string }) {
  const t = useT();

  const [users, setUsers] = useState<AdmittedUser[] | null>(null);
  const [upn, setUpn] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [isGlobalAdmin, setIsGlobalAdmin] = useState(false);

  // Null while it is being read, so the control does not flick from off to on in front of
  // somebody and look as though they changed it.
  const [selfRegistration, setSelfRegistration] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);

  async function refresh() {
    void getJson<{ allowed: boolean }>('/api/settings/self-registration')
      .then((answer) => setSelfRegistration(answer.data?.allowed ?? null));

    const result = await getJson<AdmittedUser[]>('/api/users');
    if (result.data) setUsers(result.data);
    else setMessage(result.error);
  }

  useEffect(() => { void refresh(); }, []);

  async function admit() {
    setBusy(true);
    setMessage(null);

    const result = await sendJson('/api/users', 'PUT', {
      upn: upn.trim(),
      displayName: displayName.trim() || null,
      isGlobalAdmin
    });

    if (result.error) {
      setMessage(result.error);
    } else {
      setUpn('');
      setDisplayName('');
      setIsGlobalAdmin(false);
      await refresh();
    }

    setBusy(false);
  }

  async function setAdmin(user: AdmittedUser, next: boolean) {
    setBusy(true);
    setMessage(null);

    const result = await sendJson(
      `/api/users/${encodeURIComponent(user.userId)}/global-admin`, 'PUT', { isGlobalAdmin: next });

    if (result.error) setMessage(result.error);
    else await refresh();

    setBusy(false);
  }

  async function remove(userId: string) {
    setBusy(true);
    const result = await sendJson(`/api/users/${encodeURIComponent(userId)}`, 'DELETE');

    if (result.error) setMessage(result.error);
    else await refresh();

    setConfirming(null);
    setBusy(false);
  }

  if (!users) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  return (
    <>
      <div className="page-view-header">
        <div>
          <p className="eyebrow">{t('admin.global-admin')}</p>
          <h1>{t('admin.people')}</h1>
          <p className="lede">{t('people.lede')}</p>
        </div>
      </div>

      {/*
        Above the list of people rather than below it, because it decides how people get
        onto that list. What it grants is said in full: a self-registered person has no
        engagement and sees an empty product until somebody here gives them one, and an
        administrator deciding whether to leave this on deserves to know that rather than
        having to infer it from the word "register".
      */}
      <div className="form-panel" style={{ marginBottom: '14px' }}>
        <label className="field-label">
          <span className="field-name">{t('people.self-registration')}</span>
          <select
            value={selfRegistration === null ? '' : selfRegistration ? 'on' : 'off'}
            disabled={selfRegistration === null}
            onChange={(event) => {
              const next = event.target.value === 'on';
              setSelfRegistration(next);
              void sendJson('/api/settings/self-registration', 'PUT', { allowed: next });
            }}
          >
            <option value="on">{t('people.self-registration.on')}</option>
            <option value="off">{t('people.self-registration.off')}</option>
          </select>
          <span className="hint">{t('people.self-registration.hint')}</span>
        </label>
      </div>

      <div className="form-panel">
        <div className="access-form users-form">
          <label className="field-label">
            {t('people.sign-in-name')}
            <input
              type="email"
              value={upn}
              placeholder="name@example.com"
              autoComplete="off"
              onChange={(event) => setUpn(event.target.value)}
            />
          </label>

          <label className="field-label">
            {t('common.name')}
            <input
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </label>

          <label className="field-label">
            {t('people.global-administrator')}
            <select
              value={isGlobalAdmin ? 'yes' : 'no'}
              onChange={(event) => setIsGlobalAdmin(event.target.value === 'yes')}
            >
              <option value="no">{t('common.no')}</option>
              <option value="yes">{t('common.yes')}</option>
            </select>
          </label>

          <button
            type="button"
            className="primary-button"
            disabled={busy || !upn.includes('@')}
            onClick={() => void admit()}
          >
            {t('people.admit')}
          </button>
        </div>

        <p className="panel-note">{t('people.admitting-is-not-granting-detail')}</p>
      </div>

      <div className="admin-list">
        {users.map((user) => (
          <article className="admin-row" key={user.userId}>
            <div>
              <strong>
                {user.displayName}
                {user.userId === currentUserId && <span className="tag muted">{t('people.you')}</span>}
              </strong>
              <span>{user.email ?? user.userId}</span>

              {/* A global administrator reaches everything, so listing their grants would be
                  a list that understates what they can do. Somebody with none has the
                  demonstration estate and nothing else, and saying so is the difference
                  between a blank line and an answer. */}
              <small>
                {user.isGlobalAdmin
                  ? t('people.every-engagement')
                  : user.engagementAccess.length === 0
                    ? t('people.awaiting-an-engagement')
                    : user.engagementAccess
                      .map((grant) => `${grant.engagementName}: ${t('role.' + grant.role.toLowerCase())}`)
                      .join(' · ')}
              </small>
            </div>

            <div className="admin-actions">
              <button
                type="button"
                className="secondary-button small"
                disabled={busy}
                onClick={() => void setAdmin(user, !user.isGlobalAdmin)}
              >
                {user.isGlobalAdmin ? t('people.make-ordinary') : t('people.make-administrator')}
              </button>

              {/* Nobody can remove their own access. Locking yourself out of the tool that
                  holds a client's configuration is not a mistake worth allowing. */}
              {user.userId !== currentUserId && (
                confirming === user.userId ? (
                  <button
                    type="button"
                    className="danger-button small"
                    disabled={busy}
                    onClick={() => void remove(user.userId)}
                  >
                    {t('people.confirm-remove')}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="danger-button small outline"
                    onClick={() => setConfirming(user.userId)}
                  >
                    {t('admin.remove')}
                  </button>
                )
              )}
            </div>
          </article>
        ))}
      </div>

      {confirming && <p className="panel-note">{t('people.remove-warning')}</p>}
      {message && <div className="wizard-callout warning"><strong>{message}</strong></div>}
    </>
  );
}
