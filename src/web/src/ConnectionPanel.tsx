import { useState } from 'react';
import { useCan } from './access';
import { useT, useLanguage } from './i18n';
import { ConnectionWizard } from './ConnectionWizard';
import { ConnectorMark } from './ConnectorMark';
import { getJson, sendJson, when, type Connection, type ExtractionMode } from './workspace';

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
  const [editing, setEditing] = useState<Connection | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);

  // Which one has been asked about. Removing a connection is not undoable and the button
  // sits in a row of quiet controls next to Edit, so the first click asks and the second
  // one does it. Nothing here is destructive until somebody has read the word twice.
  const [confirming, setConfirming] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const mine = connections.filter((connection) => connection.direction === direction);

  async function reload() {
    const result = await getJson<Connection[]>(`/api/engagements/${engagementId}/connections`);
    if (result.data) onChanged(result.data);
  }

  /**
   * Removes a connection nothing has read through.
   *
   * The refusal is the interesting half. A connection a run used stays, because a report
   * has to be able to say what produced it, and the API answers with how many runs rather
   * than a flat no so the message on screen can say why.
   */
  async function remove(connection: Connection) {
    setError(null);
    setConfirming(null);
    setRemoving(connection.connectionId);

    const result = await sendJson<null>(
      `/api/engagements/${engagementId}/connections/${connection.connectionId}`, 'DELETE');

    setRemoving(null);

    if (result.error) {
      setError(result.error);
      return;
    }

    await reload();
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

      {error && <p className="error">{error}</p>}

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

              {/*
                A connection could be created and never changed, so a mistyped environment
                address meant adding a second one and leaving the wrong one in the picker.
              */}
              <div className="connection-item-actions">
                {/*
                  The screen has said "not tested" since it was written and nothing could
                  ever change it: the only thing that recorded a test was the extract stage
                  of a run, so a publish target could not be proved at all and a source
                  could only be proved by analysing an estate with it.
                */}
                <TestConnection
                  engagementId={engagementId}
                  connectionId={connection.connectionId}
                  onTested={reload} />

                <button type="button" className="ghost-button" onClick={() => setEditing(connection)}>
                  {t('common.edit')}
                </button>

                {confirming === connection.connectionId ? (
                  <>
                    <button
                      type="button"
                      className="ghost-button ghost-button--danger"
                      disabled={removing === connection.connectionId}
                      onClick={() => void remove(connection)}>
                      {t('common.confirm-remove')}
                    </button>
                    <button type="button" className="ghost-button" onClick={() => setConfirming(null)}>
                      {t('common.cancel')}
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="ghost-button"
                    onClick={() => setConfirming(connection.connectionId)}>
                    {t('common.remove')}
                  </button>
                )}
              </div>
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

      {editing && (
        <ConnectionWizard
          engagementId={engagementId}
          direction={direction}

          // Only the mode it already is. The wizard hides the chooser when it is editing,
          // and handing it one mode means nothing can offer a choice that the API refuses.
          modes={modes.filter((mode) => mode.id === editing.mode)}
          editing={editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void reload(); }}
        />
      )}
    </section>
  );
}

/**
 * Proving one connection, now, rather than finding out on the next run.
 *
 * A read in every case. Testing Azure DevOps by creating a work item would be a product
 * that writes to a client's board to find out whether it can, so each target is proved by
 * listing what it can see and each source by the same call the pipeline's connect stage
 * makes.
 *
 * The answer is recorded on the connection, so the row beside this button updates and the
 * next person to look knows when it was last proved and by what.
 */
function TestConnection({ engagementId, connectionId, onTested }: {
  engagementId: string;
  connectionId: string;
  onTested: () => void;
}) {
  const t = useT();
  const canTest = useCan('Contributor');
  const [busy, setBusy] = useState(false);

  if (!canTest) return null;

  async function test() {
    setBusy(true);

    // The result is not shown here. It is written to the connection and the list reloads,
    // so the message lands in the same place it would have come from a run, which is where
    // somebody will look for it tomorrow.
    await sendJson(`/api/engagements/${engagementId}/connections/${connectionId}/test`, 'POST');

    setBusy(false);
    onTested();
  }

  return (
    <button type="button" className="ghost-button" disabled={busy} onClick={() => void test()}>
      {busy ? t('connections.testing') : t('connections.test')}
    </button>
  );
}
