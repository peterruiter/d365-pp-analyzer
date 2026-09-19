import { useState } from 'react';
import { useT } from './i18n';
import { sendJson, type ExtractionMode } from './workspace';

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
  exportedOnUtc: { type: 'date' }
};

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
  engagementId, direction, modes, onClose, onSaved
}: {
  engagementId: string;
  direction: 'source' | 'target';
  modes: ExtractionMode[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useT();

  const [step, setStep] = useState<Step>(modes.length === 1 ? 'settings' : 'mode');
  const [modeId, setModeId] = useState(modes[0]?.id ?? '');
  const [name, setName] = useState('');
  const [values, setValues] = useState<Record<string, string>>({});
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

    const result = await sendJson(`/api/engagements/${engagementId}/connections`, 'POST', {
      mode: chosen.id,
      name: name.trim() || chosen.name,
      environmentRole: direction === 'target' ? 'production' : 'unknown',
      settings: values,
      secret: secret.trim() || null
    });

    if (result.error) {
      setError(result.error);
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
            <p className="wizard-help">{t('connections.which-system-help')}</p>

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
                  {t('connections.secret')}
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
            <button type="button" className="secondary-button" onClick={() => setStep('mode')}>
              {t('connections.change-system')}
            </button>

            <span>
              {missing.length > 0
                ? t('connections.n-fields-left', missing.length)
                : t('connections.ready-to-save')}
            </span>

            <button
              type="button"
              className="primary-button"
              disabled={busy || missing.length > 0}
              onClick={() => void save()}
            >
              {t('connections.save')}
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
