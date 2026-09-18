import { useMemo, useState } from 'react';
import { useT } from './i18n';
import { ConnectorMark } from './ConnectorMark';
import { sendJson, type Connection, type ConnectorCapability, type ConnectorSetting } from './workspace';

type Step = 'connector' | 'settings' | 'done';

/**
 * The connection wizard.
 *
 * Three steps, because choosing a platform and supplying its credentials are different
 * decisions and putting them on one screen makes the second look like part of the first.
 *
 * Every field it renders comes from the connector contract, so choosing a different platform
 * produces a different form without this file knowing anything about Dataverse or Azure DevOps.
 * A connector that gains a setting gains it here too.
 *
 * The step that matters is the first. A consultant picking a platform needs to know before
 * they type anything whether this tool has a reader for it, and the contract knows: the
 * difference between "read against a real tenant" and "described, not built" is a week of
 * somebody's life.
 */
export function ConnectionWizard({
  engagementId, direction, connectors, onClose, onSaved
}: {
  engagementId: string;
  direction: 'source' | 'target';
  connectors: ConnectorCapability[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useT();

  const [step, setStep] = useState<Step>(connectors.length === 1 ? 'settings' : 'connector');
  const [connectorId, setConnectorId] = useState(connectors[0]?.id ?? '');
  const [name, setName] = useState('');
  const [values, setValues] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const chosen = useMemo(
    () => connectors.find((candidate) => candidate.id === connectorId) ?? null,
    [connectors, connectorId]);

  const missing = chosen
    ? chosen.settings.filter((setting) => setting.required && !values[setting.name]?.trim()).map((setting) => setting.name)
    : [];

  /** Prefills from the contract's own defaults, so the common case needs no typing. */
  function choose(id: string) {
    setConnectorId(id);

    const defaults: Record<string, string> = {};
    for (const setting of connectors.find((candidate) => candidate.id === id)?.settings ?? []) {
      if (setting.default !== null && setting.default !== '') defaults[setting.name] = setting.default;
    }

    setValues(defaults);
    setName('');
  }

  async function save() {
    if (!chosen) return;
    setBusy(true);
    setError(null);

    const result = await sendJson<Connection>(`/api/engagements/${engagementId}/connections`, 'POST', {
      connectorId,
      name: name.trim() || chosen.name,
      direction,
      settings: values
    });

    if (result.error) {
      setError(result.error);
    } else {
      // The secrets leave the browser the moment they have been accepted. They are in the
      // vault now and nothing on this screen needs them again.
      setValues({});
      setStep('done');
      onSaved();
    }

    setBusy(false);
  }

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <section className="forecast-modal wizard-modal" role="dialog" aria-modal="true" aria-labelledby="wizard-title">
        <div className="modal-header">
          <div>
            <p className="eyebrow">
              {t(direction === 'source' ? 'connections.new-source' : 'connections.new-target')}
            </p>
            <h2 id="wizard-title">{chosen ? chosen.name : t('connections.choose-a-system')}</h2>
          </div>
          <button className="icon-button" type="button" onClick={onClose} aria-label={t('admin.cancel')}>
            &times;
          </button>
        </div>

        <div className="wizard-steps">
          {(['connector', 'settings', 'done'] as const).map((name, index) => (
            <span key={name} className={step === name ? 'current' : ''}>
              {index + 1}. {t('connections.step-' + name)}
            </span>
          ))}
        </div>

        {step === 'connector' && (
          <div className="wizard-body">
            <h3>{t('connections.which-system')}</h3>
            <p className="wizard-help">{t('connections.which-system-help')}</p>

            <div className="connector-grid">
              {connectors.map((candidate) => (
                <button
                  type="button"
                  key={candidate.id}
                  className={`connector-card ${connectorId === candidate.id ? 'selected' : ''} ${
                    candidate.status === 'planned' ? 'planned' : ''} ${
                    candidate.status === 'preview' ? 'preview' : ''}`}
                  onClick={() => choose(candidate.id)}
                >
                  <span className="connector-card-head">
                    <ConnectorMark connectorId={candidate.id} name={candidate.name} />
                    <strong>{candidate.name}</strong>
                    {candidate.status === 'planned' && (
                      <span className="tag muted">{t('connector.status.planned')}</span>
                    )}
                    {candidate.status === 'preview' && (
                      <span className="tag warning">{t('connector.status.preview')}</span>
                    )}
                  </span>

                  <span className="connector-card-body" title={candidate.description}>
                    {candidate.description}
                  </span>
                </button>
              ))}
            </div>

            <div className="wizard-foot">
              <span>{chosen ? chosen.deployment : t('connections.choose-to-continue')}</span>
              <button
                className="primary-button"
                type="button"
                disabled={!chosen}
                onClick={() => setStep('settings')}
              >
                {t('connections.next')} <span aria-hidden="true">&rarr;</span>
              </button>
            </div>
          </div>
        )}

        {step === 'settings' && chosen && (
          <div className="wizard-body">
            <h3>{t('connections.how-do-we-reach-it')}</h3>
            <p className="wizard-help">{chosen.description}</p>

            {/* Said before a consultant spends ten minutes filling in a form. */}
            {chosen.status !== 'supported' && (
              <div className="wizard-callout warning">
                <strong>{t('connections.not-built-yet-name', chosen.name)}</strong>
                <span>{t('connections.not-built-yet-detail')}</span>
              </div>
            )}

            <label className="field-label">
              {t('common.name')}
              <input
                type="text"
                value={name}
                placeholder={chosen.name}
                onChange={(event) => setName(event.target.value)}
              />
              <span className="hint">{t('connections.what-it-is-called-here')}</span>
            </label>

            {chosen.settings.map((setting) => (
              <SettingField
                key={setting.name}
                setting={setting}
                value={values[setting.name] ?? ''}
                onChange={(next) => setValues((current) => ({ ...current, [setting.name]: next }))}
              />
            ))}

            <div className="wizard-callout">
              <strong>{t('connections.secrets-go-to-the-vault')}</strong>
              <span>{t('connections.secrets-go-to-the-vault-detail')}</span>
            </div>

            {error && <p className="curation-message danger">{error}</p>}

            <div className="wizard-foot">
              <button
                className="secondary-button"
                type="button"
                disabled={connectors.length === 1}
                onClick={() => setStep('connector')}
              >
                {t('connections.change-system')}
              </button>

              <div className="wizard-actions">
                <span className="hint">
                  {missing.length > 0
                    ? t('connections.n-fields-left', missing.length)
                    : t('connections.ready-to-save')}
                </span>
                <button
                  className="primary-button"
                  type="button"
                  disabled={busy || missing.length > 0}
                  onClick={() => void save()}
                >
                  {t('connections.save')}
                </button>
              </div>
            </div>
          </div>
        )}

        {step === 'done' && chosen && (
          <div className="wizard-body">
            <h3>{t('connections.saved')}</h3>
            <p className="wizard-help">{t('connections.saved-detail')}</p>

            <div className="wizard-foot">
              <span />
              <button className="primary-button" type="button" onClick={onClose}>
                {t('connections.close')}
              </button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}

/** One field, rendered according to what the contract says it is. */
function SettingField({
  setting, value, onChange
}: {
  setting: ConnectorSetting;
  value: string;
  onChange: (value: string) => void;
}) {
  const t = useT();

  return (
    <label className="field-label">
      {setting.name}
      {setting.required && <span className="field-note">{t('connections.required')}</span>}

      {setting.type === 'choice' ? (
        <select value={value} onChange={(event) => onChange(event.target.value)}>
          {setting.choices.map((choice) => <option key={choice} value={choice}>{choice}</option>)}
        </select>
      ) : setting.type === 'bool' ? (
        <select value={value} onChange={(event) => onChange(event.target.value)}>
          <option value="true">{t('common.yes')}</option>
          <option value="false">{t('common.no')}</option>
        </select>
      ) : (
        <input
          // A secret gets a password box and autoComplete off, so a client's client secret is
          // not offered back by the browser on the next engagement's form.
          type={setting.isSecret ? 'password' : setting.type === 'int' ? 'number' : 'text'}
          value={value}
          autoComplete={setting.isSecret ? 'new-password' : 'off'}
          onChange={(event) => onChange(event.target.value)}
        />
      )}

      <span className="hint">{setting.description}</span>
    </label>
  );
}
