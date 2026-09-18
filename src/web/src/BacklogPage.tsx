import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { getJson, type BacklogEntry } from './workspace';

/**
 * Everything no tool can migrate, with the evidence and an estimate.
 *
 * Deliberately a first class screen rather than an appendix. Naming every flow that needs
 * rebuilding is useful; saying "flows are manual" is not, and the difference is what a
 * client is actually buying when they ask what a migration costs.
 */
export function BacklogPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const [entries, setEntries] = useState<BacklogEntry[] | null>(null);

  useEffect(() => {
    let cancelled = false;

    getJson<BacklogEntry[]>(`/api/engagements/${engagementId}/backlog`).then((result) => {
      if (!cancelled) setEntries(result.data ?? []);
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  if (!entries) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  if (entries.length === 0) {
    return (
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.backlog')}</p>
            <h2>{t('backlog.nothing-in-the-backlog')}</h2>
          </div>
        </div>
        <p className="panel-note">{t('page.backlog')}</p>
      </section>
    );
  }

  // Grouped by entity, because a consultant reads this as "what do I have to rebuild"
  // rather than as a flat list of four hundred rows.
  const byEntity = entries.reduce<Record<string, BacklogEntry[]>>((groups, entry) => {
    (groups[entry.canonicalEntityId] ??= []).push(entry);
    return groups;
  }, {});

  return (
    <>
      {Object.entries(byEntity).map(([entity, items]) => (
        <section className="panel" key={entity}>
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{t('view.backlog')}</p>
              <h2>{entity}</h2>
            </div>
            <span className="tag muted">{items.length} {t('common.records')}</span>
          </div>

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('common.name')}</th>
                  <th scope="col">{t('common.reason')}</th>
                  <th scope="col">{t('backlog.evidence')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((entry, index) => (
                  <tr key={`${entry.sourceRecordId ?? 'entity'}-${index}`}>
                    <th scope="row">{entry.displayName}</th>
                    <td className="wrapping-cell">{entry.reason}</td>
                    <td><code>{entry.evidence ?? ''}</code></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </>
  );
}
