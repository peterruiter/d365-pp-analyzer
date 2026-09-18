import { useState } from 'react';
import { useT } from './i18n';
import { SystemHealthPage } from './SystemHealthPage';

type Tab = 'help' | 'health';

/**
 * Where to go when something is wrong.
 *
 * Two tabs: how to get help, and whether the deployment itself is the problem. In that
 * order, because the first thing somebody does when a screen is empty is blame themselves,
 * and the health tab is frequently the answer.
 *
 * Deliberately the same shape as the Intent Miner's support page, down to the tab bar.
 * These are two products in one suite, and a consultant moving between them should not have
 * to learn a second place to look when something breaks.
 */
export function SupportPage({ deployed, isGlobalAdmin }: { deployed: boolean; isGlobalAdmin: boolean }) {
  const t = useT();
  const [tab, setTab] = useState<Tab>('help');

  return (
    <>
      <div className="support-intro">
        <p className="eyebrow">{t('support.eyebrow')}</p>
        <h2>{t('support.getting-help')}</h2>
        <p>{t('support.lede')}</p>
      </div>

      <div className="tab-bar" role="tablist">
        {(['help', 'health'] as const).map((option) => (
          <button
            type="button"
            role="tab"
            key={option}
            aria-selected={tab === option}
            className={`tab ${tab === option ? 'active' : ''}`}
            onClick={() => setTab(option)}
          >
            {t(option === 'help' ? 'support.tab-help' : 'support.tab-health')}
          </button>
        ))}
      </div>

      <div className="tab-body">
        {tab === 'help' && (
          <>
            <div className="support-grid">
              <div>
                <span>{t('support.product-use')}</span>
                <strong>{t('support.use-the-documentation')}</strong>
              </div>
            </div>

            <footer className="support-footer">
              <strong>{t('support.capgemini-make-it-real')}</strong>
              <span>{t('support.created-by')}</span>
              <div>
                <a href="mailto:peter.ruiter@capgemini.com?subject=Solution%20Analyzer%20support">
                  {t('support.mail')}
                </a>
                <a
                  href="https://teams.microsoft.com/l/chat/0/0?users=peter.ruiter@capgemini.com"
                  target="_blank"
                  rel="noreferrer"
                >
                  {t('support.chat-on-teams')}
                </a>
                <a href="https://powerpete.com/" target="_blank" rel="noreferrer">
                  {t('support.powerpete-com')}
                </a>
              </div>
            </footer>
          </>
        )}

        {tab === 'health' && <SystemHealthPage isGlobalAdmin={isGlobalAdmin} />}
      </div>
    </>
  );
}
