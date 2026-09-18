import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { ConnectionPanel } from './ConnectionPanel';
import { levelTag } from './tags';
import { getJson, type Connection, type ConnectorCapability } from './workspace';

type Entity = { id: string; name: string; domain: string };

const levelKey: Record<string, string> = {
  Full: 'connections.read-over-an-api',
  Partial: 'connections.read-over-an-api',
  Derived: 'connections.constructed-by-this-tool',
  Manual: 'connections.needs-a-manual-export',
  None: 'connections.no-equivalent'
};

/**
 * The source systems being read, and what each will honestly give up.
 *
 * The matrix is the most useful thing on this screen and frequently the most useful thing
 * in a first client meeting. A client asks whether their IVR can be migrated, and the
 * answer differs by an order of magnitude between platforms. Saying so before an engagement
 * starts is worth more than any amount of code.
 */
export function ConnectionsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [connectors, setConnectors] = useState<ConnectorCapability[]>([]);
  const [entities, setEntities] = useState<Entity[]>([]);
  const [selected, setSelected] = useState<string>('genesys-cloud');

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const [connectionResult, connectorResult, entityResult] = await Promise.all([
        getJson<Connection[]>(`/api/engagements/${engagementId}/connections`),
        getJson<ConnectorCapability[]>('/api/connectors'),
        getJson<Entity[]>('/api/canonical-entities')
      ]);

      if (cancelled) return;
      setConnections(connectionResult.data ?? []);
      setConnectors(connectorResult.data ?? []);
      setEntities(entityResult.data ?? []);
    }

    void load();
    return () => { cancelled = true; };
  }, [engagementId]);

  if (!connections) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  const connector = connectors.find((candidate) => candidate.id === selected);

  return (
    <>
      <ConnectionPanel
        engagementId={engagementId}
        direction="source"
        connectors={connectors.filter((candidate) => candidate.id !== 'dataverse')}
        connections={connections}
        onChanged={setConnections}
      />

      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.connections')}</p>
            <h2>{t('connections.what-this-platform-gives-up')}</h2>
          </div>
          {connector && (
            <span className={`tag ${connector.status === 'supported' ? 'complete' : connector.status === 'preview' ? 'warning' : 'muted'}`}>
              {t('connector.status.' + connector.status)}
            </span>
          )}
        </div>

        <div className="filter-bar small">
          {connectors.map((candidate) => (
            <button
              key={candidate.id}
              type="button"
              className={`filter-chip ${candidate.id === selected ? 'active' : ''}`}
              onClick={() => setSelected(candidate.id)}
            >
              {candidate.name}
            </button>
          ))}
        </div>

        {connector && (
          <>
            <p className="panel-note">{connector.description}</p>

            {/* Said on every platform rather than buried in a footnote. Documentation is not
                a tenant, and a claim nobody has checked is worth scoping an engagement with
                and not worth promising a client. */}
            {!connector.verifiedAgainst && (
              <p className="panel-note standalone">{t('connections.never-verified')}</p>
            )}

            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{t('common.entity')}</th>
                    <th scope="col">{t('common.source')}</th>
                    <th scope="col" />
                  </tr>
                </thead>
                <tbody>
                  {entities.map((entity) => {
                    const level = connector.discovery[entity.id] ?? 'None';
                    const note = connector.discoveryNotes[entity.id];

                    return (
                      <tr key={entity.id}>
                        <th scope="row">{entity.name}</th>
                        <td>
                          <span className={`tag ${levelTag[level] ?? 'muted'}`}>
                            {t(levelKey[level] ?? 'connections.no-equivalent')}
                          </span>
                        </td>
                        <td className="wrapping-cell">{note ?? ''}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
    </>
  );
}
