import {FolderDto} from 'src/app/common/models';

/**
 * Name fragment of the property folder every calendar/task eForm is filed
 * under. Matched as a FRAGMENT, not an exact name: the folder is created per
 * property and real installations carry suffixed variants.
 */
export const LOGBOEGER_FOLDER_NAME = 'Logbøger';

/**
 * Depth-first search of a `getLinkedFolderDtos` tree for the first folder whose
 * name contains `nameFragment`.
 *
 * Mirrors `CalendarContainerComponent.findFolderByName`, which is private to
 * that component. Kept as a standalone function so the task-list pages can
 * resolve the same folder the calendar does (#1135) without importing the
 * container.
 */
export function findFolderByName(folders: FolderDto[], nameFragment: string): FolderDto | null {
  if (!folders) {
    return null;
  }
  for (const f of folders) {
    if (f && f.name && f.name.includes(nameFragment)) {
      return f;
    }
    if (f && f.children && f.children.length > 0) {
      const hit = findFolderByName(f.children, nameFragment);
      if (hit) {
        return hit;
      }
    }
  }
  return null;
}

/**
 * The Logbøger folder id for a property, or null when the tree has none.
 * null is the supported "no folder supplied" value on the create/edit payload —
 * the backend reads it as "keep the task's current folder" (#1135).
 */
export function findLogboegerFolderId(folders: FolderDto[]): number | null {
  const folder = findFolderByName(folders, LOGBOEGER_FOLDER_NAME);
  return folder ? folder.id : null;
}
