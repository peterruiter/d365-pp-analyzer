import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { ConnectionPanel } from './ConnectionPanel';
import { getJson, type Connection, type ExtractionMode } from './workspace';

/** How much of one evidence source a mode reaches, as a tag. */
const reachTag: Record<string, string> = {
  full: 'complete',
  partial: 'warning',
  none: 'muted'
};

/**
 * How this engagement reads the client's environment, and what each way gives up.
 *
 * The matrix is the most useful thing on this screen and frequently the most useful thing in
 * a first client meeting. What a mode cannot reach decides which rules can run at all, and
 * saying so before an engagement starts is worth more than any amount of code afterwards.
 *
 * Every figure comes from the extraction sources contract, which is the same file the report
 * reads when it writes the not-assessed section. A screen that kept its own copy would
 * eventually promise a client something the report then withdrew.
 */
export function ConnectionsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [modes, setModes] = useState<ExtractionMode[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const [connectionResult, modeResult] = await Promise.all([
        getJson<Connection[]>(`/api/engagements/${engagementId}/connections`),
        getJson<ExtractionMode[]>('/api/extraction-modes')
      ]);

      if (cancelled) return;

      setConnections(connectionResult.data ?? []);
      setModes(modeResult.data ?? []);
      setError(connectionResult.error ?? modeResult.error);
    }

    void load();
    return () => { cancelled = true; };
  }, [engagementId]);

  // Two requests, and either can fail on its own. Reporting only the connections failure
  // meant a broken extraction-modes endpoint produced a wizard with no systems in it and no
  // explanation anywhere on the page: it looked like a layout fault, and the actual cause
  // was a 500 nobody was shown.
  if (error) {
    return <p className="error">{error}</p>;
  }

  if (!connections) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  const sources = Object.keys(modes[0]?.reaches ?? {});

  return (
    <>
      <ConnectionPanel
        engagementId={engagementId}
        direction="source"
        modes={modes.filter((mode) => mode.id !== 'azureDevOps')}
        connections={connections}
        onChanged={setConnections}
      />

      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.connections')}</p>
            <h2>{t('connections.what-this-platform-gives-up')}</h2>
          </div>
        </div>

        <div className="wizard-body">
          <table className="findings-table">
            <thead>
              <tr>
                <th>{t('common.name')}</th>
                {sources.map((source) => (
                  <th key={source}>{t('source.' + source)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {modes.map((mode) => (
                <tr key={mode.id}>
                  <th scope="row" className="reach-mode">
                    <strong>{mode.name}</strong>
                    <span className="hint">{mode.summary}</span>
                  </th>

                  {sources.map((source) => (
                    <td key={source}>
                      <span className={`tag ${reachTag[mode.reaches[source]] ?? 'muted'}`}>
                        {t('source.reach.' + (mode.reaches[source] ?? 'none'))}
                      </span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/*
          Said on the screen rather than only in the report. A mode that cannot reach an
          evidence source does not produce fewer findings, it produces findings nobody ran,
          and the difference is what this whole product exists to keep visible.
        */}
        <p className="panel-note">{t('connections.reach-decides-what-runs')}</p>
      </section>
    </>
  );
}
