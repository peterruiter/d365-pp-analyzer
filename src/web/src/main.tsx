import { StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import './workspaces.css';
// Last, so this product's corrections win over the two copied stylesheets.
import './shell.css';
import { LanguageProvider, LanguagePicker, useT, type Translate } from './i18n';
import { ThemeProvider, ThemeToggle, useTheme } from './theme';
import { OverviewPage } from './OverviewPage';
import { AccessProvider } from './access';
import { RunsPage } from './RunsPage';
import { ConnectionsPage } from './ConnectionsPage';
import { FindingsPage } from './FindingsPage';
import { BacklogPage } from './BacklogPage';
import { ReportsPage } from './ReportsPage';
import { AdministrationPage } from './AdministrationPage';
import { DocumentationPage } from './DocumentationPage';
import { SupportPage } from './SupportPages';
import type { Engagement } from './workspace';

// Overview first because that is where you land and where the checklist lives. The rest
// follow the order the work actually happens in: read a source, point at a target, see what
// would change, run it, then deal with what no tool can do.
//
// That order was not obvious. An earlier arrangement put the plan before the target, and a
// consultant opening the product for the first time met the thing that needs a Dynamics
// environment before the screen where they would have connected one.
const navigation = ['Overview', 'Connections', 'Runs', 'Findings', 'Backlog', 'Reports'];

// Everything the address bar may name. The three below the navigation are reachable by
// their own links rather than by the rail.
const addressable = [...navigation, 'Administration', 'Documentation', 'Support'];

/**
 * A workspace name shown to the reader.
 *
 * Navigation state stays in English so a language change cannot invalidate a bookmark or
 * the address bar. Only the label changes language.
 */
function viewLabel(t: Translate, name: string): string {
  const key = 'view.' + name.toLowerCase();
  const translated = t(key);
  // A rail reading "view.overview" is worse than one reading "Overview", so an untranslated
  // key falls back to the name rather than to itself.
  return translated === key ? name : translated;
}

const pageDescriptionKeys: Record<string, string> = {
  Overview: 'page.overview',
  Connections: 'page.connections',
  Runs: 'page.runs',
  Findings: 'page.findings',
  Backlog: 'page.backlog',
  Reports: 'page.reports',
  Administration: 'page.administration',
  Documentation: 'page.documentation',
  Support: 'page.support'
};

/** The workspace the address bar names, or the overview when it names none. */
function viewFromHash(): string {
  const name = decodeURIComponent(window.location.hash.replace('#', '')).trim();
  // A stale bookmark or a typo lands on the overview rather than on a blank screen.
  return addressable.includes(name) ? name : 'Overview';
}

function NavItem({ workspace, label, active, disabled, disabledReason, onOpen }: {
  workspace: string;
  label: string;
  active: boolean;
  disabled: boolean;
  disabledReason: string;
  onOpen: () => void;
}) {
  return (
    <button
      type="button"
      className={`nav-item ${active ? 'active' : ''}`}
      disabled={disabled}
      title={disabled ? disabledReason : undefined}
      onClick={onOpen}
      aria-current={active ? 'page' : undefined}
      data-workspace={workspace}
    >
      {label}
    </button>
  );
}

/** The frame a reader sees before they are signed in, or when they cannot be. */
function AuthShell({ children }: { children: React.ReactNode }) {
  const t = useT();
  return (
    <div className="auth-shell">
      <header className="topbar">
        <span className="wordmark">
          <img className="wordmark-logo" src={`${import.meta.env.BASE_URL}capgemini-white.svg`} alt={t('shell.capgemini')} />
          <img className="wordmark-spade" src={`${import.meta.env.BASE_URL}capgemini-spade-white.svg`} alt={t('shell.capgemini')} />
          <span className="product-name">{t('app.name')}</span>
        </span>
        <div className="topbar-actions">
          <LanguagePicker />
          <ThemeToggle />
        </div>
      </header>
      {/* auth-gate, not a bare main: the shell is a column so the top bar spans, and
          this is the part that centres. */}
      <main className="auth-gate">{children}</main>
    </div>
  );
}

function App() {
  const t = useT();
  const { adopt } = useTheme();

  const [activeView, setActiveView] = useState(viewFromHash);
  const [menuOpen, setMenuOpen] = useState(false);
  const [engagements, setEngagements] = useState<Engagement[]>([]);
  const [engagement, setEngagement] = useState<Engagement | null>(null);
  const [engagementsOpen, setEngagementsOpen] = useState(false);
  const [administrationMode, setAdministrationMode] = useState<'list' | 'create'>('list');

  const [authConfigured, setAuthConfigured] = useState(false);
  const [authenticated, setAuthenticated] = useState(false);
  const [registered, setRegistered] = useState(false);
  const [authFailed, setAuthFailed] = useState(false);
  const [checking, setChecking] = useState(true);
  const [displayName, setDisplayName] = useState('');
  const [userId, setUserId] = useState('');
  const [isGlobalAdmin, setIsGlobalAdmin] = useState(false);
  const [adminContact, setAdminContact] = useState('');
  const [apiState, setApiState] = useState<'checking' | 'online' | 'offline'>('checking');

  // replaceState rather than pushState: moving between workspaces is not navigation a reader
  // expects the back button to unwind one step at a time.
  useEffect(() => {
    const target = `#${activeView}`;
    if (window.location.hash !== target) {
      window.history.replaceState(null, '', target);
    }
  }, [activeView]);

  useEffect(() => {
    const onHashChange = () => setActiveView(viewFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [versionResponse, authResponse] = await Promise.all([
          fetch('/api/version'),
          fetch('/api/auth/status')
        ]);

        if (!versionResponse.ok || !authResponse.ok) throw new Error(t('shell.api-unavailable'));

        const auth = await authResponse.json();
        if (cancelled) return;

        setApiState('online');
        setAuthConfigured(Boolean(auth.authConfigured));
        setAuthenticated(Boolean(auth.authenticated));
        setRegistered(Boolean(auth.registered));
        setDisplayName(auth.displayName ?? '');
        setUserId(auth.userId ?? '');
        setIsGlobalAdmin(Boolean(auth.isGlobalAdmin));
        setAdminContact(auth.adminContact ?? '');

        // Taken from the sign-in response rather than written back, so adopting a stored
        // preference does not immediately save it again.
        adopt(auth.theme);

        if (!auth.authConfigured || (auth.authenticated && auth.registered)) {
          const engagementResponse = await fetch('/api/engagements');
          if (engagementResponse.ok) {
            const list: Engagement[] = await engagementResponse.json();
            if (cancelled) return;
            setEngagements(list);
            setEngagement((current) => current ?? list[0] ?? null);
          }
        }
      } catch {
        if (cancelled) return;
        setApiState('offline');
        setAuthFailed(true);
      } finally {
        if (!cancelled) setChecking(false);
      }
    }

    void load();
    return () => { cancelled = true; };
  }, [adopt, t]);

  if (checking) {
    return <AuthShell><div className="auth-card"><p className="lede">{t('shell.signing-in')}</p></div></AuthShell>;
  }

  if (authConfigured && !authenticated) {
    return (
      <AuthShell>
        <div className="auth-card">
          <p className="eyebrow">{t('shell.secure-sign-in')}</p>
          <h1>{t('app.name')}</h1>
          <p className="lede">{t('app.tagline')}</p>
          <a className="primary-button auth-signin" href="/account/signin">{t('shell.sign-in-with-microsoft')}</a>
        </div>
      </AuthShell>
    );
  }

  if (authConfigured && authenticated && !registered) {
    // Signing in is not the same as being admitted. A tool that reads a client's contact
    // centre configuration is not one anybody in the tenant should be able to wander into.
    return (
      <AuthShell>
        <div className="auth-card">
          <p className="eyebrow">{t('shell.access-required')}</p>
          <h1>{t('shell.no-access-title')}</h1>
          <p className="lede">{t('shell.your-microsoft-sign-in-succeeded-but')}</p>
          {adminContact
            ? <a className="primary-button auth-signin" href={`mailto:${adminContact}?subject=Solution Analyzer access request`}>{adminContact}</a>
            : <p>{t('shell.ask-a-global-administrator-to-add')}</p>}
          <a className="secondary-button" href="/account/signout">{t('shell.sign-out')}</a>
        </div>
      </AuthShell>
    );
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <a className="wordmark" href="/" aria-label={t('shell.home')}>
          <img className="wordmark-logo" src={`${import.meta.env.BASE_URL}capgemini-white.svg`} alt={t('shell.capgemini')} />
          {/* The same mark without the wordmark, for narrow screens. Only one of the two is
              ever visible, so the alt text lives on whichever that is. */}
          <img className="wordmark-spade" src={`${import.meta.env.BASE_URL}capgemini-spade-white.svg`} alt={t('shell.capgemini')} />
          <span className="product-name">{t('app.name')}</span>
        </a>
        <div className="topbar-actions">
          <LanguagePicker />
          <ThemeToggle />
          {authConfigured
            ? <span className="account-menu"><span>{displayName}</span><a href="/account/signout">{t('shell.sign-out')}</a></span>
            : <span className="dev-account">{t(authFailed ? 'shell.api-unavailable' : 'shell.local-development')}</span>}

          {/* Last in the row and last in the tab order, which is where a reader expects the
              control that opens everything else. */}
          <button
            className="menu-toggle"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="primary-navigation"
            onClick={() => setMenuOpen(!menuOpen)}
          >
            <span aria-hidden="true" /><span aria-hidden="true" /><span aria-hidden="true" />
            <span className="visually-hidden">{menuOpen ? t('shell.close-the-menu') : t('shell.open-the-menu')}</span>
          </button>
        </div>
      </header>

      <div className="workspace">
        <aside
          className={`sidebar ${menuOpen ? 'open' : ''}`}
          id="primary-navigation"
          aria-label={t('shell.primary-navigation')}
        >
          <div className="engagement-switcher">
            <span className="eyebrow">{t('shell.engagement')}</span>
            <button type="button" className="engagement-button" onClick={() => setEngagementsOpen(!engagementsOpen)}>
              {engagement?.name ?? t('shell.no-engagement-assigned')} <span aria-hidden="true">⌄</span>
            </button>
            <span className="engagement-meta">
              {engagement
                ? t('shell.role-access', t('role.' + (engagement.accessRole ?? 'viewer').toLowerCase()))
                : t('shell.open-administration-to-get-started')}
            </span>
            {engagementsOpen && (
              <div className="engagement-menu">
                {engagements.map((item) => (
                  <button
                    type="button"
                    key={item.engagementId}
                    className={`engagement-menu-item ${item.engagementId === engagement?.engagementId ? 'active' : ''}`}
                    onClick={() => { setEngagement(item); setActiveView('Overview'); setEngagementsOpen(false); }}
                  >
                    {item.name}
                    <span>
                      {item.isDemonstration ? t('shell.demonstration') : (item.clientName ?? '')}
                    </span>
                  </button>
                ))}
                <button
                  type="button"
                  className="engagement-menu-item"
                  onClick={() => { setAdministrationMode('list'); setActiveView('Administration'); setEngagementsOpen(false); }}
                >
                  {t('shell.browse-all-engagements')}<span>{t('shell.open-administration')}</span>
                </button>
              </div>
            )}
          </div>

          <nav>
            {navigation.map((item) => (
              <NavItem
                key={item}
                workspace={item}
                label={viewLabel(t, item)}
                active={activeView === item}
                disabled={!engagement}
                disabledReason={t('shell.pick-an-engagement-first')}
                onOpen={() => { setActiveView(item); setMenuOpen(false); }}
              />
            ))}
          </nav>

          <div className="sidebar-footer">
            <button className="sidebar-link" type="button" onClick={() => { setAdministrationMode('list'); setActiveView('Administration'); setMenuOpen(false); }}>{t('shell.administration')}</button>
            <button className="sidebar-link" type="button" onClick={() => { setActiveView('Documentation'); setMenuOpen(false); }}>{t('shell.documentation')}</button>
            <button className="sidebar-link" type="button" onClick={() => { setActiveView('Support'); setMenuOpen(false); }}>{t('shell.support')}</button>

            {/* Under the three links people open when they suspect something is wrong, rather
                than in the top bar competing with the two controls they actually press. */}
            <span className={`system-status ${apiState}`}>
              <span className="status-dot" />
              {t(apiState === 'online' ? 'shell.api-connected' : apiState === 'checking' ? 'shell.api-checking' : 'shell.api-preview')}
            </span>
          </div>
        </aside>

        <AccessProvider role={engagement?.accessRole ?? null} isGlobalAdmin={isGlobalAdmin}>
        <main className="main-content">
          <div className="breadcrumb">
            <span>{t('shell.engagements')}</span>
            <span aria-hidden="true">/</span>
            <strong>{engagement?.name ?? t('shell.administration')}</strong>
          </div>

          <section className="page-heading">
            <div>
              <p className="eyebrow">{t('shell.workspace-eyebrow', viewLabel(t, activeView))}</p>
              <h1>{activeView === 'Overview' ? t('app.tagline') : viewLabel(t, activeView)}</h1>
              <p className="lede">{t(pageDescriptionKeys[activeView] ?? activeView)}</p>
            </div>
          </section>

          {activeView === 'Documentation' ? <DocumentationPage />
            : activeView === 'Support' ? <SupportPage deployed={authConfigured} isGlobalAdmin={isGlobalAdmin} />
            : activeView === 'Administration' || !engagement
              ? <AdministrationPage
                  mode={administrationMode}
                  onModeChange={setAdministrationMode}
                  engagements={engagements}
                  setEngagements={setEngagements}
                  currentEngagement={engagement}
                  onSwitch={(selected) => { setEngagement(selected); setActiveView(selected ? 'Overview' : 'Administration'); }}
                  isGlobalAdmin={isGlobalAdmin}
                  currentUserId={userId}
                />
            : activeView === 'Connections' ? <ConnectionsPage engagementId={engagement.engagementId} />
            : activeView === 'Runs' ? <RunsPage engagementId={engagement.engagementId} />
            : activeView === 'Findings' ? <FindingsPage engagementId={engagement.engagementId} />
            : activeView === 'Backlog' ? <BacklogPage engagementId={engagement.engagementId} />
            : activeView === 'Reports' ? <ReportsPage engagementId={engagement.engagementId} />
            : <OverviewPage
                engagementId={engagement.engagementId}
                onNavigate={(workspace) => setActiveView(workspace)}
              />}
        </main>
        </AccessProvider>
      </div>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LanguageProvider>
      <ThemeProvider>
        <App />
      </ThemeProvider>
    </LanguageProvider>
  </StrictMode>
);
