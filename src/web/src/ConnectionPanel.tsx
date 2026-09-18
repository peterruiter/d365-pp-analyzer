import { useState } from 'react';
import { useT, useLanguage } from './i18n';
import { ConnectionWizard } from './ConnectionWizard';
import { ConnectorMark } from './ConnectorMark';
import { getJson, when, type Connection, type ExtractionMode } from './workspace';

/**
 * The connections in one direction, and the way to add another.
 *
 * Separate from the page so the source half and the target half are the same component with
 * a different direction, rather than two lists that drift apart. A target connection is the
 * only place this product writes anything, so the two lists looking alike is deliberate:
 * whatever is true of one is worth reading in the other.
 */
export function ConnectionPanel({
  engagementId, direction, modes, connections, onChanged
}: {
  engagementId: string;
  direction: 'source' | 'target';
  modes: ExtractionMode[];
  connections: Connection[];
  onChanged: (connections: Connection[]) => void;
}) {
  const t = useT();
  const { culture } = useLanguage();
  const [adding, setAdding] = useState(false);

  const mine = connections.filter((connection) => connection.direction === direction);

  async function reload() {
    const result = await getJson<Connection[]>(`/api/engagements/${engagementId}/connections`);
    if (result.data) onChanged(result.data);
  }

  return (
    <section className="panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">{t('view.connections')}</p>
          <h2>{t(direction === 'source' ? 'connections.new-source' : 'connections.new-target')}</h2>
        </div>
        <button type="button" className="primary-button" onClick={() => setAdding(true)}>
          {t('connections.add')}
        </button>
      </div>

      {mine.length === 0 ? (
        <p className="dashboard-empty">{t('common.nothing-here-yet')}</p>
      ) : (
        <ul className="connection-list">
          {mine.map((connection) => (
            <li className="connection-item" key={connection.connectionId}>
              <ConnectorMark connectorId={connection.mode} name={connection.name} />

              <div className="connection-item-body">
                <strong>{connection.name}</strong>

                {/*
                  A connection that has never been tested is not a connection that works. It
                  says so rather than showing nothing, because an empty column reads as fine.
                */}
                <span className="hint">
                  {connection.lastTestedUtc === null
                    ? t('connections.not-tested')
                    : t('connections.last-tested', when(connection.lastTestedUtc, culture, t('common.never')))}
                </span>

                {connection.lastTestMessage && (
                  <span className="hint">{connection.lastTestMessage}</span>
                )}
              </div>

              <span className={`tag ${connection.lastTestSucceeded === true
                ? 'complete'
                : connection.lastTestSucceeded === false ? 'danger' : 'muted'}`}>
                {connection.lastTestSucceeded === true
                  ? t('runs.status.succeeded')
                  : connection.lastTestSucceeded === false
                    ? t('runs.status.failed')
                    : t('connections.not-tested')}
              </span>
            </li>
          ))}
        </ul>
      )}

      {adding && (
        <ConnectionWizard
          engagementId={engagementId}
          direction={direction}
          modes={modes}
          onClose={() => setAdding(false)}
          onSaved={() => { setAdding(false); void reload(); }}
        />
      )}
    </section>
  );
}
