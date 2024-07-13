export class FilesCreateModel {
  file: File;
  // only for front-end(for display)
  src?: Uint8Array;
  tagIds: number[];
  propertyIds: number[];
}
