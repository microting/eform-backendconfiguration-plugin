/** Mirrors C# `AdhocAreaModel`. */
export interface AdhocAreaModel {
  id: number;
  propertyId: number;
  name: string;
}

/** Mirrors C# `AdhocAreaCreateModel` (`POST areas`). */
export interface AdhocAreaCreateModel {
  propertyId: number;
  names: string[];
}

/** Mirrors C# `AdhocAreaRenameModel` (`PUT areas/{id}`). */
export interface AdhocAreaRenameModel {
  name: string;
}
