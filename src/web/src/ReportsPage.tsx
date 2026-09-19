import { useEffect, useState } from 'react';
import { useT, useLanguage } from './i18n';
import { getJson, when } from './workspace';

type AvailableReport = {
  id: string;
  file: string;
  rows: number;
  /**
   * Whether it can actually be produced. Only the PDF sends this: it needs a licence key and
   * without one the endpoint returns an error rather than a document. A download that fails
   * is worse than one that is not offered, because the reader cannot tell whose fault it is.
   */
  available?: boolean;
};

type ReportsState = {
  available: boolean;
  producedFrom?: string;
  totalRecords?: number;
  caveat?: string;
  reports: AvailableReport[];
};

/**
 * The documents that leave the product.
 *
 * A screen is where findings are read. A workbook is what gets forwarded, filtered, argued
 * with in a workshop and pasted into a statement of work, and it has to stand up with the
 * product nowhere near it. That is why every artefact carries its own caveat rather than
 * relying on somebody remembering this page.
 *
 * Downloads go through a plain link rather than fetch and a blob. The browser then names the
 * file from the Content-Disposition the API set, which is the client and the date, and a
 * reader with six documents in a folder can tell which is which.
 */
export function ReportsPage({ engagementId }: { engagementId: string }) {
  const t = useT();
  const { culture } = useLanguage();

  const [state, setState] = useState<ReportsState | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getJson<ReportsState>(`/api/engagements/${engagementId}/reports`).then((result) => {
      if (cancelled) return;
      setState(result.data);
      setError(result.error);
    });

    return () => { cancelled = true; };
  }, [engagementId]);

  if (error) {
    return (
      <section className="panel">
        <div className="panel-heading"><div><h2>{t('common.something-went-wrong')}</h2></div></div>
        <p className="dashboard-empty">{error}</p>
      </section>
    );
  }

  if (!state) {
    return <section className="panel"><p className="dashboard-empty">{t('common.loading')}</p></section>;
  }

  if (!state.available) {
    return (
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.reports')}</p>
            <h2>{t('reports.nothing-to-report-yet')}</h2>
          </div>
        </div>

        <p className="panel-note">{t('reports.nothing-to-report-lede')}</p>
      </section>
    );
  }

  const describe: Record<string, { title: string; summary: string; detail: string }> = {
    inventory: {
      title: 'reports.inventory',
      summary: 'reports.inventory-summary',
      detail: 'reports.inventory-detail'
    },
    pdf: {
      title: 'reports.pdf',
      summary: 'reports.pdf-summary',
      detail: 'reports.pdf-detail'
    }
  };

  return (
    <>
      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.reports')}</p>
            <h2>{t('reports.heading')}</h2>
          </div>
          <span className="tag muted">
            {t('reports.from-discovery', when(state.producedFrom ?? null, culture, t('common.never')))}
          </span>
        </div>

        {/* Repeated on every artefact as well as here. A sheet gets copied out of a workbook
            on its own, and an estimate that arrives without the sentence saying what it
            rests on is an estimate somebody will quote. */}
        {state.caveat && <p className="panel-note standalone">{state.caveat}</p>}

        <div className="documentation-list">
          {state.reports.map((report) => {
            const text = describe[report.id];
            if (!text) return null;

            // Absent means it does not carry the flag, which is every report that needs
            // nothing to produce it. Only an explicit false hides a row.
            if (report.available === false) return null;

            return (
              <a
                className="documentation-row"
                key={report.id}
                href={`/api/engagements/${engagementId}/reports/${report.file}`}
                download
              >
                <span>
                  <strong>{t(text.title)}</strong>
                  <small>{t(text.summary)}</small>
                </span>
                <span className="documentation-get" aria-hidden="true">&darr;</span>
              </a>
            );
          })}
        </div>
      </section>

      <section className="panel">
        <div className="panel-heading">
          <div>
            <p className="eyebrow">{t('view.reports')}</p>
            <h2>{t('reports.what-is-in-them')}</h2>
          </div>
        </div>

        <div className="wizard-body">
          {state.reports.map((report) => {
            const text = describe[report.id];
            if (!text) return null;

            // Absent means it does not carry the flag, which is every report that needs
            // nothing to produce it. Only an explicit false hides a row.
            if (report.available === false) return null;

            return (
              <div className="wizard-callout" key={report.id}>
                <strong>{t(text.title)}</strong>
                <span>{t(text.detail)}</span>
              </div>
            );
          })}

          {/* Only when there is no PDF to offer. Said here rather than discovered when
              somebody asks for one. */}
          {!state.reports.some((report) => report.id === 'pdf' && report.available !== false) && (
            <div className="wizard-callout warning">
              <strong>{t('reports.no-pdf')}</strong>
              <span>{t('reports.no-pdf-detail')}</span>
            </div>
          )}
        </div>
      </section>
    </>
  );
}
