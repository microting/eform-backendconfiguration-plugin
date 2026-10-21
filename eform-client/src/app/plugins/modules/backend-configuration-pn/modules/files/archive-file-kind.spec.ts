import {describe, it, expect} from 'vitest';
import {ARCHIVE_UPLOAD_MIME_TYPES, archiveFileKind, archivePresentation} from './archive-file-kind';

describe('archiveFileKind', () => {
  it.each([
    ['pdf', 'pdf'],
    ['PDF', 'pdf'],
    ['.pdf', 'pdf'],
    ['jpg', 'image'],
    ['JPG', 'image'],
    ['jpeg', 'image'],
    ['.Jpeg', 'image'],
    ['png', 'image'],
    ['.PNG', 'image'],
    ['docx', 'other'],
    ['gif', 'other'],
    ['', 'other'],
  ])('%s is %s', (extension, kind) => {
    expect(archiveFileKind(extension)).toBe(kind);
  });

  it('treats a missing extension as other', () => {
    expect(archiveFileKind(null)).toBe('other');
    expect(archiveFileKind(undefined)).toBe('other');
  });

  it('accepts pdf, jpeg and png uploads', () => {
    expect(ARCHIVE_UPLOAD_MIME_TYPES).toEqual(['application/pdf', 'image/jpeg', 'image/png']);
  });

  it.each([
    ['pdf', 'picture_as_pdf', 'View PDF', 'Delete file'],
    ['PNG', 'image', 'View image', 'Delete image'],
    ['docx', 'description', 'View file', 'Delete this file'],
  ])('%s is presented with %s, %s, %s', (extension, icon, viewKey, deleteKey) => {
    expect(archivePresentation(extension)).toEqual({icon, viewKey, deleteKey});
  });
});
