/** Which top folders the user folded in the Arkiv tree; per browser, never shared. */
export const ARCHIVE_FOLD_STORAGE_KEY = 'bcpn.archive.collapsedFolders';

export function readCollapsedFolderIds(): number[] {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(ARCHIVE_FOLD_STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((id): id is number => Number.isInteger(id)) : [];
  } catch {
    return [];
  }
}

export function writeCollapsedFolderIds(ids: number[]): void {
  try {
    window.localStorage.setItem(ARCHIVE_FOLD_STORAGE_KEY, JSON.stringify([...new Set(ids)]));
  } catch {
    // Storage blocked (private window): folding still works for this page view.
  }
}
