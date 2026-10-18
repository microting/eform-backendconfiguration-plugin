import {describe, it, expect} from 'vitest';
import {ArchiveFolderModel, InboxSuggestionKind, InboxSuggestionModel} from '../models';
import {serverDay} from '../modules/tail-bite/shared/tail-bite-dates';
import {inboxDateSuggestion, inboxFolderSuggestion, preselectedFolderId} from './inbox-classification.helper';

function suggestion(kind: InboxSuggestionKind, confidence: number, extra: Partial<InboxSuggestionModel> = {}): InboxSuggestionModel {
  return {
    id: 1, kind, targetId: 0, targetName: '', source: 0, confidence,
    evidence: null, page: null, reason: null, targetDate: null, ...extra,
  };
}

const folders = [{id: 4, name: 'Fakturaer', parentId: 3} as ArchiveFolderModel];

describe('inbox classification helpers', () => {
  it('picks the strongest folder and date suggestion', () => {
    const list = [
      suggestion(InboxSuggestionKind.Folder, 0.3, {targetId: 9}),
      suggestion(InboxSuggestionKind.Folder, 0.5, {targetId: 4}),
      suggestion(InboxSuggestionKind.DocumentDate, 0.2, {targetDate: '2026-10-09T00:00:00'}),
      suggestion(InboxSuggestionKind.DocumentDate, 0.8, {targetDate: '2026-10-03T00:00:00'}),
      suggestion(InboxSuggestionKind.Property, 0.9, {targetId: 4}),
    ];
    expect(inboxFolderSuggestion(list)?.targetId).toBe(4);
    expect(inboxDateSuggestion(list)?.targetDate).toBe('2026-10-03T00:00:00');
  });

  it('passes a Date target date through, read with serverDay by the dialog', () => {
    const date = new Date(Date.UTC(2026, 9, 3));
    const picked = inboxDateSuggestion([suggestion(InboxSuggestionKind.DocumentDate, 0.6, {targetDate: date})]);
    expect(serverDay(picked!.targetDate!)).toBe('2026-10-03');
  });

  it('a date suggestion without a date does not count', () => {
    expect(inboxDateSuggestion([suggestion(InboxSuggestionKind.DocumentDate, 0.9)])).toBeNull();
    expect(inboxFolderSuggestion([])).toBeNull();
  });

  it('preselects a known folder at the preselect threshold only', () => {
    expect(preselectedFolderId(suggestion(InboxSuggestionKind.Folder, 0.5, {targetId: 4}), folders)).toBe(4);
    expect(preselectedFolderId(suggestion(InboxSuggestionKind.Folder, 0.3, {targetId: 4}), folders)).toBeNull();
    expect(preselectedFolderId(suggestion(InboxSuggestionKind.Folder, 0.9, {targetId: 99}), folders)).toBeNull();
    expect(preselectedFolderId(null, folders)).toBeNull();
  });
});
