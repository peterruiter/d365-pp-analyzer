import { useEffect, useState } from 'react';
import { useT } from './i18n';
import { getJson, range, type Backlog, type BacklogItem } from './workspace';

/**
 * The work items, before anybody publishes anything.
 *
 * Deliberately a first class screen rather than an appendix. Naming every component that
 * needs work, with what "done" means for it, is useful; saying "there is technical debt" is
 * not, and the difference is what a client is actually buying when they ask what it costs.
 *
 * Nothing on this screen has been published. Publishing is a separate act behind an approval
 * bound to the exact backlog somebody read, which is why this page shows the backlog and
 * offers no button that writes anywhere.
 */
export function BacklogPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const [backlog, setBacklog] = useState<Backlog | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    void getJson<Backlog>(`/api/engagements/${engagementId}/backlog`).then((result) => {
      if (cancelled) return;
      if (result.data) setBacklog(result.data);
      else setError(result.error);
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  if (error) return <p className="error">{error}</p>;

  if (!backlog) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  if (backlog.items.length === 0) {
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

  // Grouped by work item type, because a consultant reads this as "what kind of work is
  // this" rather than as a flat list of four hundred rows. Within a group, priority order:
  // the first thing on the screen should be the first thing to do.
  const byType = backlog.items.reduce<Record<string, BacklogItem[]>>((groups, item) => {
    (groups[item.workItemType] ??= []).push(item);
    return groups;
  }, {});

  return (
    <>
      {Object.entries(byType).map(([type, items]) => (
        <section className="panel" key={type}>
          <div className="panel-heading">
            <div>
              <p className="eyebrow">{t('view.backlog')}</p>
              <h2>{type}</h2>
            </div>
            <span className="tag muted">{items.length} {t('common.records')}</span>
          </div>

          <div className="table-wrap">
            <table className="findings-table">
              <thead>
                <tr>
                  <th scope="col">{t('common.name')}</th>
                  <th scope="col">{t('backlog.acceptance')}</th>
                  <th scope="col" className="numeric">{t('backlog.points')}</th>
                  <th scope="col" className="numeric">{t('findings.hours')}</th>
                </tr>
              </thead>
              <tbody>
                {[...items].sort((a, b) => a.priority - b.priority).map((item) => (
                  <tr key={item.backlogItemId}>
                    <th scope="row">{item.title}</th>

                    <td className="wrapping-cell">
                      {item.acceptanceCriteria}

                      {/*
                        The test requirement is shown with the criterion rather than under it.
                        The backlog builder refuses to produce an item without one, so an item
                        that shows none is a bug rather than a gap in somebody's writing.
                      */}
                      {item.testRequirement && (
                        <span className="hint">{item.testRequirement}</span>
                      )}
                    </td>

                    <td className="numeric">{item.storyPoints ?? '—'}</td>
                    <td className="numeric">{range(item.lowHours, item.highHours)}</td>
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
