import {ArchiveFolderModel, InboxSuggestionKind, InboxSuggestionModel} from '../models';
import {INBOX_PRESELECT_CONFIDENCE} from './inbox-display.helper';
import {folderOptions} from './archive-folders.helper';

function strongest(suggestions: InboxSuggestionModel[]): InboxSuggestionModel | null {
  return [...suggestions].sort((a, b) => b.confidence - a.confidence)[0] ?? null;
}

/** The folder suggestion (0..1 per document, D9). */
export function inboxFolderSuggestion(suggestions: InboxSuggestionModel[]): InboxSuggestionModel | null {
  return strongest(suggestions.filter(s => s.kind === InboxSuggestionKind.Folder));
}

/** The document-date suggestion (0..1 per document, D8); one without a date is ignored. */
export function inboxDateSuggestion(suggestions: InboxSuggestionModel[]): InboxSuggestionModel | null {
  return strongest(suggestions.filter(s => s.kind === InboxSuggestionKind.DocumentDate && !!s.targetDate));
}

/**
 * Like properties and etiketter: the suggestion at ≥ INBOX_PRESELECT_CONFIDENCE, when the picker can show its folder.
 * Without one, the picker's first folder: the default folder when the caller sees it. null only without folders.
 */
export function preselectedFolderId(suggestion: InboxSuggestionModel | null, folders: ArchiveFolderModel[]): number | null {
  return suggestion
    && suggestion.confidence >= INBOX_PRESELECT_CONFIDENCE
    && folders.some(f => f.id === suggestion.targetId)
    ? suggestion.targetId
    : folderOptions(folders)[0]?.id ?? null;
}
