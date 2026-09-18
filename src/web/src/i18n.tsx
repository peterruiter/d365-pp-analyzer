import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

// The English strings are compiled in rather than fetched.
//
// Everything else is downloaded, but English has to be here before the first paint: the sign-in
// screen renders before any request has come back, and a reader who is not signed in would
// otherwise be looking at "shell.secure-sign-in" until two round trips finished, or for good if
// the API is unreachable. This is the same file the server serves, so the two cannot drift.
import english from '../../PowerPete.Analyzer.Domain/Localization/Resources/ui.en.json';

// The language list is compiled in for the same reason, and one more: the switcher has to be on
// the sign-in screen. That is the screen a reader who cannot get in is looking at, and the one
// where being unable to change language is worst. Fetching the list would make the switcher
// disappear whenever the request behind it did.
import contract from '../../../build/contracts/locales.json';

export type Locale = {
  code: string;
  englishName: string;
  nativeName: string;
  culture: string;
};

type LocaleState = {
  default: string;
  current: string;
  chosen: string | null;
  locales: Locale[];
};

export type Translate = (key: string, ...args: Array<string | number>) => string;

type LanguageContextValue = {
  language: string;
  culture: string;
  locales: Locale[];
  ready: boolean;
  t: Translate;
  setLanguage: (code: string) => void;
};

const fallback: LanguageContextValue = {
  language: 'en',
  culture: 'en-GB',
  locales: [],
  ready: false,
  t: (key) => key,
  setLanguage: () => undefined
};

const LanguageContext = createContext<LanguageContextValue>(fallback);

/**
 * The language everything on screen is rendered in.
 *
 * The choice lives on the server, against the person rather than the browser, so it survives a
 * sign-in from somewhere else. That is also the only option available: this application is not
 * allowed to use local storage.
 *
 * English is compiled in and every other language is fetched over the top of it, so there is
 * never a moment where a screen shows a resource key, and an unreachable API degrades to
 * English rather than to nonsense.
 */
export function LanguageProvider({ children }: { children: ReactNode }) {
  const [bundle, setBundle] = useState<Record<string, string>>(english as Record<string, string>);
  const [language, setLanguageState] = useState('en');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetch('/api/locales');
        if (cancelled || !response.ok) return;
        const payload = await response.json() as LocaleState;
        setLanguageState(payload.current);
      } catch {
        // An unreachable API is already reported by the status indicator in the top bar. There
        // is nothing useful to say here, and English is a reasonable place to stand.
      }
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;

    if (language === 'en') {
      setBundle(english as Record<string, string>);
      return;
    }

    void (async () => {
      try {
        const response = await fetch(`/api/locales/${language}/ui`);
        if (cancelled || !response.ok) return;

        // Over the English baseline, not instead of it. The server composes the same way, but
        // doing it here as well means a language stays readable even if the response is partial.
        const translated = await response.json() as Record<string, string>;
        setBundle({ ...(english as Record<string, string>), ...translated });
      } catch {
        // Leave English in place. An unreachable API is reported by the status indicator.
      }
    })();

    return () => { cancelled = true; };
  }, [language]);

  const setLanguage = useCallback((code: string) => {
    setLanguageState(code);
    void fetch('/api/me/language', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ languageCode: code })
    });
  }, []);

  const value = useMemo<LanguageContextValue>(() => {
    // The contract is the list. The API response only says which one this person is on.
    const locales = contract.locales.map((locale) => ({
      code: locale.code,
      englishName: locale.englishName,
      nativeName: locale.nativeName,
      culture: locale.culture
    }));
    const culture = locales.find((locale) => locale.code === language)?.culture ?? 'en-GB';

    // {0}, {1} and so on, the same placeholders the server side formatter uses, so one string
    // can be shared between a screen and a document without being written twice.
    const t: Translate = (key, ...args) => {
      const text = bundle[key] ?? key;
      return args.length === 0
        ? text
        : text.replace(/\{(\d+)\}/g, (match, index: string) => String(args[Number(index)] ?? match));
    };

    return { language, culture, locales, ready: true, t, setLanguage };
  }, [bundle, language, setLanguage]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

/** Everything a component needs to render in the reader's language. */
export function useLanguage() {
  return useContext(LanguageContext);
}

/** The common case: just the translate function. */
export function useT(): Translate {
  return useContext(LanguageContext).t;
}

/**
 * A number, a percentage or a date in the reader's language.
 *
 * These come back from the API as plain numbers and ISO timestamps on purpose. Formatting them
 * here rather than on the server is what makes a language change instant, and it keeps the API
 * returning values rather than presentation.
 */
export function useFormats() {
  const { culture } = useLanguage();

  return useMemo(() => ({
    number: (value: number, digits = 0) =>
      value.toLocaleString(culture, { minimumFractionDigits: digits, maximumFractionDigits: digits }),
    percent: (share: number, digits = 0) =>
      share.toLocaleString(culture, { style: 'percent', minimumFractionDigits: digits, maximumFractionDigits: digits }),
    date: (value?: string | null) =>
      value ? new Date(value).toLocaleDateString(culture, { day: 'numeric', month: 'short', year: 'numeric' }) : '—'
  }), [culture]);
}

/**
 * The language switcher.
 *
 * A short list of native names rather than flags: a flag is a country and several of these
 * languages are spoken in more than one.
 */
export function LanguagePicker() {
  const { language, locales, setLanguage, t } = useLanguage();

  if (locales.length < 2) return null;

  return (
    <label className="language-picker">
      <span className="visually-hidden">{t('language.choose')}</span>
      <select value={language} onChange={(event) => setLanguage(event.target.value)}>
        {locales.map((locale) => (
          <option key={locale.code} value={locale.code}>{locale.nativeName}</option>
        ))}
      </select>
    </label>
  );
}
