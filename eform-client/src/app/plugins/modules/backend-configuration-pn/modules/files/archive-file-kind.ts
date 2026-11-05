export type ArchiveFileKind = 'pdf' | 'image' | 'other';

/** What an archive document can be previewed as, from its file extension (case-insensitive, a leading dot is fine). */
export function archiveFileKind(extension: string | null | undefined): ArchiveFileKind {
  const ext = (extension ?? '').trim().replace(/^\./, '').toLowerCase();
  if (ext === 'pdf') {
    return 'pdf';
  }
  if (ext === 'jpg' || ext === 'jpeg' || ext === 'png') {
    return 'image';
  }
  return 'other';
}

/** The MIME types the archive upload accepts. */
export const ARCHIVE_UPLOAD_MIME_TYPES: readonly string[] = ['application/pdf', 'image/jpeg', 'image/png'];

/** What the table row and the delete dialog show for a document kind. ('Delete file' is the PDF wording.) */
export interface ArchiveFilePresentation {
  icon: string;
  viewKey: string;
  deleteKey: string;
}

const PRESENTATION: Record<ArchiveFileKind, ArchiveFilePresentation> = {
  pdf: {icon: 'picture_as_pdf', viewKey: 'View PDF', deleteKey: 'Delete file'},
  image: {icon: 'image', viewKey: 'View image', deleteKey: 'Delete image'},
  other: {icon: 'description', viewKey: 'View file', deleteKey: 'Delete this file'},
};

export function archivePresentation(extension: string | null | undefined): ArchiveFilePresentation {
  return PRESENTATION[archiveFileKind(extension)];
}

/** Revokes an object URL and returns null, so a field can be reset in one statement. */
export function revokeObjectUrl(url: string | null): null {
  if (url) {
    URL.revokeObjectURL(url);
  }
  return null;
}
