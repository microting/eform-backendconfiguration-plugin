import {da} from '../../i18n/da';
import {deDE} from '../../i18n/deDE';
import {enUS} from '../../i18n/enUS';
import {ARCHIVE_I18N_KEYS} from './archive-i18n-keys';

const LOCALES: Record<string, Record<string, unknown>> = {enUS, da, deDE};

describe('archive folders i18n', () => {
  it.each(Object.keys(LOCALES))('%s has a non-empty value for every archive key', locale => {
    const missing = ARCHIVE_I18N_KEYS.filter(key => {
      const value = LOCALES[locale][key];
      return typeof value !== 'string' || value.trim() === '';
    });
    expect(missing).toEqual([]);
  });

  it('English values equal their keys (the fallback for every other locale)', () => {
    const differing = ARCHIVE_I18N_KEYS.filter(key => (enUS as Record<string, unknown>)[key] !== key);
    expect(differing).toEqual([]);
  });

  it('Danish uses the agreed words and the API\'s exact refusal texts', () => {
    const t = da as Record<string, unknown>;
    expect(t['Read access']).toBe('Læseadgang');
    expect(t['Worker groups']).toBe('Medarbejdergrupper');
    expect(t['Tags']).toBe('Etiketter');
    expect(t['Date needs checking']).toBe('Dato skal tjekkes');
    expect(t['Save and next']).toBe('Gem og næste');
    expect(t['At most 2 levels']).toBe('Højst 2 niveauer');
    expect(t['Choose a folder']).toBe('Vælg en mappe');
    expect(t['The default folder cannot be deleted']).toBe('Standardmappen kan ikke slettes');
    expect(t['Only empty folders can be deleted ({{documents}} documents, {{subfolders}} subfolders)'])
      .toBe('Kun tomme mapper kan slettes ({{documents}} dokumenter, {{subfolders}} undermapper)');
    expect(t['The move changes who can read the documents. Only an administrator can do that.'])
      .toBe('Flytningen ændrer, hvem der kan læse dokumenterne. Kun en administrator kan gøre det.');
    expect(t['{{count}} documents can now be read by {{who}}']).toBe('{{count}} dokumenter kan nu læses af {{who}}');
  });
});
