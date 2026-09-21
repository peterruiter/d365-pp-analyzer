import { useState } from 'react';
import { useT } from './i18n';
import { sendJson, type Connection, type ExtractionMode } from './workspace';
import sources from '../../../build/contracts/extraction-sources.json';

type Step = 'mode' | 'settings' | 'done';

/**
 * How each declared setting is asked for.
 *
 * The contract names the settings a mode needs and says nothing about how to collect them,
 * which is right: it describes the product rather than the screen. Without this table the
 * wizard rendered the contract's own identifiers as labels, so a consultant was asked for
 * "tenantId" and "exportedOnUtc" in a text box and had to guess the format.
 *
 * A setting with no entry here still renders, as a text box under its own name. That is the
 * honest fallback: adding a setting to the contract should not make the wizard refuse to
 * show it, it should make it look unfinished, which is what it is.
 */
const fields: Record<string, { type: string; placeholder?: string }> = {
  tenantId: { type: 'text', placeholder: '00000000-0000-0000-0000-000000000000' },
  clientId: { type: 'text', placeholder: '00000000-0000-0000-0000-000000000000' },
  environmentUrl: { type: 'url', placeholder: 'https://contoso.crm4.dynamics.com' },
  declaredEnvironmentName: { type: 'text', placeholder: 'Contoso production' },
  exportedOnUtc: { type: 'date' },
  organisationUrl: { type: 'url', placeholder: 'https://dev.azure.com/contoso' },
  project: { type: 'text', placeholder: 'Contoso Platform' },
  owner: { type: 'text', placeholder: 'contoso' },
  repository: { type: 'text', placeholder: 'platform-backlog' },

  // Empty is the right answer for github.com, so the placeholder shows the shape of the
  // only thing that belongs here rather than suggesting something has to be typed.
  apiBaseUrl: { type: 'url', placeholder: 'https://github.contoso.com/api/v3' }
};

/**
 * Where the checker may run.
 *
 * From the contract rather than from a list here, and the contract's list is Microsoft's
 * own, minus the government and China endpoints: those live on different domains and the
 * client this product builds addresses one, so offering them would offer a value that
 * quietly produces a URL pointing nowhere.
 *
 * This setting existed for months and could not be set. The worker read it, refused the
 * checker without it, and said so in the run; no screen had ever asked for it, because it
 * was in no mode's settings in the contract and the wizard renders exactly what the
 * contract declares. So the checker could never run against a live environment, every rule
 * that depends on it reported as not assessed, and the reason given was a field nobody
 * could find.
 */
const geographies: { id: string; name: string }[] = sources.checkerGeographies;

/**
 * The connection wizard.
 *
 * Two decisions, on two steps, because choosing how to reach an environment and supplying the
 * credentials for it are different questions and putting them on one screen makes the second
 * look like part of the first.
 *
 * Every field it renders comes from the extraction sources contract, so a mode that gains a
 * setting gains it here without this file knowing anything about tenants or solution files.
 * The contract says it generates this wizard; this is that.
 */
export function ConnectionWizard({
  engagementId, direction, modes, editing, onClose, onSaved
}: {
  engagementId: string;
  direction: 'source' | 'target';
  modes: ExtractionMode[];

  /**
   * The connection being changed, or absent to add one.
   *
   * The same form either way rather than a second one beside it. An edit form that drifts
   * from the add form is how a field ends up settable once and never again, which is the
   * defect this whole thing exists to fix.
   */
  editing?: Connection;
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useT();

  // Editing never shows the mode step. Each mode carries a different shape of settings and
  // a different kind of credential, so changing how an estate is reached is a new
  // connection rather than an edit of this one, and the API refuses it either way.
  const [step, setStep] = useState<Step>(editing || modes.length === 1 ? 'settings' : 'mode');
  const [modeId, setModeId] = useState(editing?.mode ?? modes[0]?.id ?? '');
  const [name, setName] = useState(editing?.name ?? '');
  const [values, setValues] = useState<Record<string, string>>(editing?.settings ?? {});
  const [secret, setSecret] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploaded, setUploaded] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const chosen = modes.find((candidate) => candidate.id === modeId) ?? null;
  const missing = chosen ? chosen.settings.filter((setting) => !values[setting]?.trim()) : [];

  function choose(id: string) {
    setModeId(id);
    setValues({});
    setStep('settings');
  }

  /**
   * Sends the chosen file and keeps the blob name it comes back with.
   *
   * On choosing the file rather than on saving. A solution export is tens of megabytes and
   * putting that behind the Save button means a long unexplained wait on the one press that
   * is supposed to be instant, with nothing to show whether it is working.
   */
  async function upload(file: File) {
    setUploading(true);
    setError(null);

    const body = new FormData();
    body.append('file', file);

    try {
      const response = await fetch(`/api/engagements/${engagementId}/uploads`, { method: 'POST', body });
      const payload = await response.json() as { blobName?: string; error?: string };

      if (!response.ok || !payload.blobName) {
        setError(payload.error ?? `${response.status} ${response.statusText}`);
        setUploaded(null);
      } else {
        // The blob name is what the connection stores and the worker opens. The file itself
        // never touches this form again.
        setValues((current) => ({ ...current, uploadedFile: payload.blobName! }));
        setUploaded(file.name);
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setUploaded(null);
    }

    setUploading(false);
  }

  async function save() {
    if (!chosen) return;

    setBusy(true);
    setError(null);

    const result = editing
      ? await sendJson<{ connectionId: string }>(
        `/api/engagements/${engagementId}/connections/${editing.connectionId}`, 'PUT', {
          name: name.trim() || chosen.name,
          environmentRole: editing.environmentRole,
          settings: values,

          // Blank means keep the credential that is already in the vault. Asking for it
          // again to change a name would mean pasting a client's secret to fix a typo.
          secret: secret.trim() || null
        })
      : await sendJson<{ connectionId: string }>(
        `/api/engagements/${engagementId}/connections`, 'POST', {
          mode: chosen.id,
          name: name.trim() || chosen.name,
          environmentRole: direction === 'target' ? 'production' : 'unknown',
          settings: values,
          secret: secret.trim() || null
        });

    if (result.error) {
      setError(result.error);
    } else if (chosen.id === 'delegated' && (editing || result.data !== null)) {
      // Straight to Microsoft. The connection exists now and carries the environment
      // address; what it does not have is anybody's permission to read it, and the only
      // place that can be granted is the sign-in page. A full page navigation rather than a
      // popup, because a popup is the thing every browser blocks and every consultant has
      // already switched off.
      window.location.href = `/api/connections/${editing?.connectionId ?? result.data!.connectionId}/authorize`;
      return;
    } else {
      // The credential leaves the browser the moment it has been accepted. It is in the vault
      // now and nothing on this screen needs it again.
      setSecret('');
      setValues({});
      setStep('done');
      onSaved();
    }

    setBusy(false);
  }

  return (
    // The backdrop is what makes this a dialog rather than a block in the page. Without it
    // the wizard renders inline between the panels that launched it, which on a narrow
    // screen reads as the layout having broken rather than as something having opened.
    <div className="modal-backdrop" role="presentation">
    <section className="forecast-modal wizard-modal" role="dialog" aria-modal="true" aria-labelledby="wizard-title">
      <div className="modal-header">
        <div>
          <p className="eyebrow">{t('view.connections')}</p>
          <h2 id="wizard-title">{chosen && step !== 'mode' ? chosen.name : t('connections.choose-a-system')}</h2>
        </div>
        <button className="icon-button" type="button" onClick={onClose} aria-label={t('connections.close')}>
          &times;
        </button>
      </div>

      {step === 'mode' && (
        <>
          <div className="wizard-body">
            <h3>{t('connections.which-system')}</h3>
            <p className="wizard-help">
              {t(direction === 'target' ? 'connections.which-system-help-target' : 'connections.which-system-help')}
            </p>

            <div className="connector-grid">
              {modes.map((mode) => (
                <button
                  key={mode.id}
                  type="button"
                  className={`connector-card ${mode.id === modeId ? 'selected' : ''}`}
                  disabled={mode.status !== 'supported'}
                  title={mode.status !== 'supported' ? t('connections.not-built-yet-detail') : undefined}
                  onClick={() => choose(mode.id)}
                >
                  <span className="connector-card-head">
                    <strong>{mode.name}</strong>
                  </span>
                  <span className="hint">{mode.summary}</span>
                  {mode.status !== 'supported' && (
                    <span className="tag warning">{t('connections.not-built-yet-short')}</span>
                  )}
                </button>
              ))}
            </div>
          </div>

          <div className="wizard-foot">
            <span>{chosen ? chosen.summary : t('connections.choose-to-continue')}</span>
          </div>
        </>
      )}

      {step === 'settings' && chosen && (
        <>
          <div className="wizard-body">
            <h3>{t('connections.how-do-we-reach-it')}</h3>
            <p className="wizard-help">{chosen.summary}</p>

            <label className="field-label">
              {t('common.name')}
              <input type="text" value={name} onChange={(event) => setName(event.target.value)} />
              <span className="hint">{t('connections.what-it-is-called-here')}</span>
            </label>

            {chosen.settings.map((setting) => (
              <label className="field-label" key={setting}>
                {t('field.' + setting)}

                {setting === 'uploadedFile' ? (
                  <>
                    {/*
                      A file input, because this setting is a file. It was a text box asking
                      for a name, and typing a name into it produced a connection pointing at
                      a blob nobody had uploaded.
                    */}
                    <input
                      type="file"
                      accept=".zip,application/zip"
                      disabled={uploading}
                      onChange={(event) => {
                        const file = event.target.files?.[0];
                        if (file) void upload(file);
                      }}
                    />
                    <span className="hint">
                      {uploading
                        ? t('connections.uploading')
                        : uploaded !== null
                          ? t('connections.file-chosen', uploaded)
                          : t('field.uploadedFile.hint')}
                    </span>
                  </>
                ) : setting === 'checkerGeography' ? (
                  <>
                    {/*
                      A list, because this is a choice from a fixed set and a text box would
                      accept "Europe" or "eu" and produce a hostname that does not resolve.
                      No default selected: where a client's solution is uploaded for analysis
                      is theirs to decide and the product picking the nearest one for them is
                      exactly the silent default this refuses to make.
                    */}
                    <select
                      value={values[setting] ?? ''}
                      onChange={(event) => setValues({ ...values, [setting]: event.target.value })}>
                      <option value="">{t('connections.choose-a-geography')}</option>
                      {geographies.map((geography) => (
                        <option key={geography.id} value={geography.id}>{geography.name}</option>
                      ))}
                    </select>
                    <span className="hint">{t('field.checkerGeography.hint')}</span>
                  </>
                ) : (
                  <>
                    <input
                      type={fields[setting]?.type ?? 'text'}
                      placeholder={fields[setting]?.placeholder}
                      value={values[setting] ?? ''}
                      onChange={(event) => setValues({ ...values, [setting]: event.target.value })}
                    />
                    <span className="hint">{t('field.' + setting + '.hint')}</span>
                  </>
                )}
              </label>
            ))}

            {chosen.needsSecret && (
              <>
                <label className="field-label">
                  {/* Named for what it actually is. "Client secret" is right for an Entra
                      app registration and wrong for a personal access token and wronger
                      for an Atlassian API token, and somebody pasting the wrong kind of
                      credential gets an authentication error rather than a hint. */}
                  {t('connections.secret.' + chosen.authType)}
                  <input
                    type="password"
                    value={secret}
                    autoComplete="off"
                    onChange={(event) => setSecret(event.target.value)}
                  />
                </label>

                <div className="wizard-callout">
                  <strong>{t('connections.secrets-go-to-the-vault')}</strong>
                  <span>{t('connections.secrets-go-to-the-vault-detail')}</span>
                </div>
              </>
            )}

            {error && <p className="curation-message danger">{error}</p>}
          </div>

          <div className="wizard-foot">
            {/* Not when editing. The mode of an existing connection cannot change, so an
                offer to change it leads to a chooser with one option in it. */}
            {editing ? <span /> : (
              <button type="button" className="secondary-button" onClick={() => setStep('mode')}>
                {t('connections.change-system')}
              </button>
            )}

            <span>
              {missing.length > 0
                ? t('connections.n-fields-left', missing.length)
                : chosen.authType === 'authorizationCode'
                  ? t('connections.you-will-be-sent-to-microsoft')
                  : t('connections.ready-to-save')}
            </span>

            <button
              type="button"
              className="primary-button"
              disabled={busy || missing.length > 0}
              onClick={() => void save()}
            >
              {chosen.authType === 'authorizationCode'
                ? t('connections.sign-in-to-environment')
                : t('connections.save')}
            </button>
          </div>
        </>
      )}

      {step === 'done' && (
        <>
          <div className="wizard-body">
            <h3>{t('connections.saved')}</h3>
            <p className="wizard-help">{t('connections.saved-detail')}</p>
          </div>

          <div className="wizard-foot">
            <button className="primary-button" type="button" onClick={onClose}>
              {t('connections.close')}
            </button>
          </div>
        </>
      )}
    </section>
    </div>
  );
}
