import {describe, it, expect} from 'vitest';
import {DocumentDateState, FileModel} from '../models';
import {dateOnlyOf, hasPanelChanges, isPanelValid, panelChanges, panelValueOf} from './archive-panel.helper';

const file: FileModel = {
  id: 7, fileName: 'Faktura 118', fileExtension: 'pdf', properties: [1, 2], tags: [{id: 3, name: 'Forsyning'} as any],
  folderId: 10, folderPath: ['Drift'], documentDate: '2026-10-03T00:00:00',
  documentDateState: DocumentDateState.Suggested, readGroups: [{id: 5, name: 'Ledelse'}], ownAccess: false,
};

describe('archive panel helpers', () => {
  it('dateOnlyOf keeps the date part', () => {
    expect(dateOnlyOf('2026-10-03T00:00:00')).toBe('2026-10-03');
    expect(dateOnlyOf('2026-10-03')).toBe('2026-10-03');
    expect(dateOnlyOf(null)).toBeNull();
    expect(dateOnlyOf(undefined)).toBeNull();
  });

  it('dateOnlyOf reads the UTC day of the Date the host\'s DateInterceptor produces', () => {
    expect(dateOnlyOf(new Date(Date.UTC(2026, 9, 3)))).toBe('2026-10-03');
    expect(dateOnlyOf(new Date(NaN))).toBeNull();
  });

  it('panelValueOf copies the document; inherited groups are not own groups', () => {
    expect(panelValueOf(file)).toEqual({
      name: 'Faktura 118', propertyIds: [1, 2], tagIds: [3], folderId: 10,
      documentDate: '2026-10-03', documentDateState: DocumentDateState.Suggested, ownAccess: false, readTagIds: [],
    });
    expect(panelValueOf({...file, ownAccess: true}).readTagIds).toEqual([5]);
  });

  it('panelValueOf reads a Date document date as its server day, so an untouched date is no change', () => {
    const dated = {...file, documentDate: new Date(Date.UTC(2026, 9, 3))};
    const value = panelValueOf(dated);
    expect(value.documentDate).toBe('2026-10-03');
    expect(hasPanelChanges(panelChanges(value, panelValueOf(dated)))).toBe(false);
    expect(panelChanges(value, {...value, documentDate: '2026-10-04'}).date).toBe(true);
    expect(panelValueOf({...file, documentDate: null}).documentDate).toBeNull();
  });

  it('reports exactly what changed', () => {
    const original = panelValueOf(file);
    expect(hasPanelChanges(panelChanges(original, {...original}))).toBe(false);
    expect(panelChanges(original, {...original, name: ' Faktura 118 '}).nameOrProperties).toBe(false);
    expect(panelChanges(original, {...original, propertyIds: [2, 1]}).nameOrProperties).toBe(false);
    expect(panelChanges(original, {...original, propertyIds: [1]}).nameOrProperties).toBe(true);
    expect(panelChanges(original, {...original, tagIds: []}).tags).toBe(true);
    expect(panelChanges(original, {...original, folderId: 11}).folder).toBe(true);
    // Approving the suggestion keeps the date but changes the state.
    expect(panelChanges(original, {...original, documentDateState: DocumentDateState.Approved}).date).toBe(true);
    expect(panelChanges(original, {...original, ownAccess: true, readTagIds: [5]}).access).toBe(true);
  });

  it('own groups only count as a change while own access is on', () => {
    const own = panelValueOf({...file, ownAccess: true});
    expect(panelChanges(own, {...own, readTagIds: [5, 6]}).access).toBe(true);
    const inherited = panelValueOf(file);
    expect(panelChanges(inherited, {...inherited, readTagIds: [9]}).access).toBe(false);
  });

  it('needs a name, a property and a folder', () => {
    const v = panelValueOf(file);
    expect(isPanelValid(v)).toBe(true);
    expect(isPanelValid({...v, name: '  '})).toBe(false);
    expect(isPanelValid({...v, propertyIds: []})).toBe(false);
    expect(isPanelValid({...v, folderId: 0})).toBe(false);
  });
});
