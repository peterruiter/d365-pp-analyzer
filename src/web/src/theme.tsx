import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useT } from './i18n';

/**
 * Light or dark, for the product.
 *
 * Three states, not two. `null` means nobody has chosen, and the stylesheet answers to the
 * operating system's own setting in that case. Collapsing that into a default of "light" is the
 * usual mistake: it overrides every reader who runs their machine dark before they have ever
 * expressed an opinion, and it makes "follow the system" impossible to get back to.
 *
 * The choice is stored on the person, server side, next to their language. The web app is not
 * allowed to use local storage, and a preference that does not survive signing in on a second
 * machine is not really a preference.
 */
export type Theme = 'light' | 'dark';

type ThemeContextValue = {
  /** What the reader chose, or null when they are following the operating system. */
  theme: Theme | null;
  /** What is actually on screen right now, with the system setting resolved. */
  resolved: Theme;
  setTheme: (theme: Theme) => void;
  /** Takes the stored preference from the sign-in response without writing it back. */
  adopt: (theme: Theme | null | undefined) => void;
};

const fallback: ThemeContextValue = {
  theme: null,
  resolved: 'light',
  setTheme: () => undefined,
  adopt: () => undefined
};

const ThemeContext = createContext<ThemeContextValue>(fallback);

function systemTheme(): Theme {
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme | null>(null);
  const [system, setSystem] = useState<Theme>(() => systemTheme());

  // Only matters while nobody has chosen, but it has to be live: somebody whose machine switches
  // to dark at sunset should not have to reload to follow it.
  useEffect(() => {
    const query = window.matchMedia?.('(prefers-color-scheme: dark)');
    if (!query) return;
    const onChange = () => setSystem(systemTheme());
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  const resolved = theme ?? system;

  // The stylesheet already answers to prefers-color-scheme on its own, so the first paint is
  // right before this runs. The attribute exists to let an explicit choice overrule the machine.
  useEffect(() => {
    const root = document.documentElement;
    if (theme === null) root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', theme);
  }, [theme]);

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next);
    void fetch('/api/me/theme', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ theme: next })
    });
  }, []);

  const adopt = useCallback((next: Theme | null | undefined) => {
    if (next === 'light' || next === 'dark') setThemeState(next);
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({ theme, resolved, setTheme, adopt }),
    [theme, resolved, setTheme, adopt]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  return useContext(ThemeContext);
}

/**
 * The sun and moon pill, the same control Capgemini puts in its own header.
 *
 * A switch rather than a button, because that is what it is: it reports its state to a screen
 * reader rather than describing an action, and a live region says which way it went. Both icons
 * stay visible and the ball moves, so the control reads as a position rather than as a guess
 * about what clicking it will do.
 */
export function ThemeToggle() {
  const { resolved, setTheme } = useTheme();
  const t = useT();
  const dark = resolved === 'dark';

  return (
    <button
      type="button"
      role="switch"
      aria-checked={dark}
      className="theme-toggle"
      title={dark ? t('theme.switch-to-light') : t('theme.switch-to-dark')}
      onClick={() => setTheme(dark ? 'light' : 'dark')}
    >
      <span className="visually-hidden">{dark ? t('theme.dark-mode-on') : t('theme.light-mode-on')}</span>
      <span className="theme-toggle-icon" aria-hidden="true">{sun}</span>
      <span className="theme-toggle-icon" aria-hidden="true">{moon}</span>
      <span className="theme-toggle-ball" aria-hidden="true" />
    </button>
  );
}

const sun = (
  <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
  </svg>
);

const moon = (
  <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
  </svg>
);
