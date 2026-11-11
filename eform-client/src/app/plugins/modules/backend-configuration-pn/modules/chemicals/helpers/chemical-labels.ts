import {ChemicalLabelKind, ChemicalLabelModel, ChemicalRegisterEntryModel} from '../../../models';

/** One shared empty list: a template binding must get the same array on every change detection. */
const NO_LABELS: ChemicalLabelModel[] = [];

/** The labels of an entry; none for an entry from a server that predates the label documents. */
export function labelsOf(entry: ChemicalRegisterEntryModel | null | undefined): ChemicalLabelModel[] {
  return entry?.labels ?? NO_LABELS;
}

/**
 * One line of the label menu: the kind (label with its packaging, or minor use with its packaging) and the revision
 * date, else the revision, e.g. "Etiket (5 L) · 01-03-2026". Packaging and date keep two documents apart.
 */
export function labelOptionText(label: ChemicalLabelModel, translate: {instant(key: string, params?: object): string}): string {
  const params = {packaging: label.packaging};
  const minorUse = label.kind === ChemicalLabelKind.MinorUse;
  const name = label.packaging
    ? translate.instant(minorUse ? 'Chemicals: Minor use ({{packaging}})' : 'Chemicals: Label ({{packaging}})', params)
    : translate.instant(minorUse ? 'Chemicals: Minor use' : 'Chemicals: Label');
  const date = /^(\d{4})-(\d{2})-(\d{2})/.exec(label.revisionDate ?? '');
  const revision = date ? `${date[3]}-${date[2]}-${date[1]}` : label.revision;
  return revision ? `${name} · ${revision}` : name;
}
