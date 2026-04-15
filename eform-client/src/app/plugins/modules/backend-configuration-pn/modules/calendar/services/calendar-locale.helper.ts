import {TranslateService} from '@ngx-translate/core';

/**
 * Returns a BCP-47 locale string for use with JS toLocaleDateString() /
 * Intl.DateTimeFormat based on the user's current translate language.
 * Falls back to en-GB if no language is set.
 */
export function getCurrentLocale(translate: TranslateService): string {
  const lang = translate.currentLang || translate.defaultLang || 'en';
  const map: Record<string, string> = {
    da: 'da-DK',
    en: 'en-GB',
    de: 'de-DE',
  };
  return map[lang] ?? lang;
}
