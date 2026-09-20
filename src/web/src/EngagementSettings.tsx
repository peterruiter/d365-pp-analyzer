import { useState } from 'react';
import { useT } from './i18n';
import { sendJson, type Engagement } from './workspace';
import locales from '../../../build/contracts/locales.json';

/**
 * What an engagement is called, who it is for, and the languages its documents come out in.
 *
 * None of this could be changed once the engagement existed. A client's name typed wrong on
 * the first day stayed wrong on the cover of every report afterwards, and the report
 * language had to be chosen at creation, before anybody knew who was going to read it.
 *
 * Two languages rather than one, because they are genuinely different questions. A Dutch
 * client reads a Dutch report; the delivery team that picks up the backlog in Azure DevOps
 * is frequently not Dutch, and work items in a language half the team cannot read are worse
 * than no work items.
 */
export function EngagementSettings({ engagement, onClose, onSaved }: {
  engagement: Engagement;
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useT();
  const [name, setName] = useState(engagement.name);
  const [clientName, setClientName] = useState(engagement.clientName ?? '');
  const [isRegulated, setIsRegulated] = useState(engagement.isRegulated ?? false);
  const [reportLanguage, setReportLanguage] = useState(engagement.reportLanguage ?? 'en');
  const [backlogLanguage, setBacklogLanguage] = useState(engagement.backlogLanguage ?? 'en');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);

    const result = await sendJson(`/api/engagements/${engagement.engagementId}`, 'PUT', {
      name: name.trim(),
      clientName: clientName.trim() || null,
      isRegulated,
      reportLanguage,
      backlogLanguage
    });

    setBusy(false);

    if (result.error) setError(result.error);
    else onSaved();
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="forecast-modal" role="dialog" aria-modal="true" aria-labelledby="engagement-title">
        <div className="modal-header">
          <div>
            <p className="eyebrow">{t('admin.engagement-settings')}</p>
            <h2 id="engagement-title">{engagement.name}</h2>
          </div>
          <button className="icon-button" type="button" onClick={onClose} aria-label={t('common.cancel')}>
            &times;
          </button>
        </div>

        <div className="wizard-body">
          {/*
            Its own layout rather than the access row's. .access-form is three columns with
            align-items: end, which is right for "user, role, Grant" and wrong for five
            fields of different shapes: the two with a hint under them pushed their inputs
            up, so no label and no box on this dialog lined up with its neighbour.
          */}
          <div className="settings-form">
            <label className="field-label">
              {t('common.name')}
              <input value={name} onChange={(event) => setName(event.target.value)} />
            </label>

            <label className="field-label">
              {t('admin.client-name')}
              <input value={clientName} onChange={(event) => setClientName(event.target.value)} />
              <span className="hint">{t('admin.client-name-help')}</span>
            </label>

            <label className="field-label">
              {t('admin.report-language')}
              <select value={reportLanguage} onChange={(event) => setReportLanguage(event.target.value)}>
                {locales.locales.map((language) => (
                  <option key={language.code} value={language.code}>{language.nativeName}</option>
                ))}
              </select>
            </label>

            <label className="field-label">
              {t('admin.backlog-language')}
              <select value={backlogLanguage} onChange={(event) => setBacklogLanguage(event.target.value)}>
                {locales.locales.map((language) => (
                  <option key={language.code} value={language.code}>{language.nativeName}</option>
                ))}
              </select>
              <span className="hint">{t('admin.backlog-language-help')}</span>
            </label>

            {/* A multiplier on every estimate, so it is worth being explicit that ticking
                it changes numbers that are already on screen elsewhere. */}
            <label className="field-label checkbox">
              <input
                type="checkbox"
                checked={isRegulated}
                onChange={(event) => setIsRegulated(event.target.checked)}
              />
              {t('admin.regulated')}
              <span className="hint">{t('admin.regulated-help')}</span>
            </label>
          </div>

          {error && <p className="error">{error}</p>}
        </div>

        <div className="wizard-foot">
          <button type="button" className="secondary-button" onClick={onClose}>{t('common.cancel')}</button>
          <span />
          <button
            type="button"
            className="primary-button"
            disabled={busy || name.trim().length === 0}
            onClick={() => void save()}>
            {t('admin.save-engagement')}
          </button>
        </div>
      </section>
    </div>
  );
}
