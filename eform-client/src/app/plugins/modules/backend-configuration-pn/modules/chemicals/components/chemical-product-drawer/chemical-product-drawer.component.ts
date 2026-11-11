import {Component, ElementRef, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {ChemicalInventoryModel, ChemicalLabelModel, ChemicalPlacementModel, ChemicalSdsDocumentModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalInventoryRow,
  ChemicalStockHistoryRow,
  currentUiLocale,
  labelOptionText,
  labelsOf,
  openChemicalLabel,
  openSafetyDataSheet,
  otherSdsDocuments,
  placementStockHistory,
  placementsOfChemical,
  sdsCandidates,
  sdsFileNameForLocale,
  sdsLanguageText,
  stockUnitLabel,
} from '../../helpers';
import {NgIf, NgFor, NgSwitch, NgSwitchCase, DatePipe, DecimalPipe} from '@angular/common';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatTooltip} from '@angular/material/tooltip';
import {MatMenu, MatMenuItem, MatMenuTrigger} from '@angular/material/menu';
import {ChemicalGhsPictogramsComponent} from '../chemical-ghs-pictograms/chemical-ghs-pictograms.component';

export interface ChemicalProductDrawerData {
  row: ChemicalInventoryRow;
  inventory: ChemicalInventoryModel;
  stockEnabled: boolean;
}

export type ChemicalDrawerAction = 'move' | 'stock' | 'remove';

export type ChemicalDrawerResult = {action: ChemicalDrawerAction; row: ChemicalInventoryRow} | undefined;

export type ChemicalDrawerTab = 'overview' | 'placements' | 'stock';

/** One tab of the side panel; `elementId` is the tab button's id (the e2e page object uses it). */
export interface ChemicalDrawerTabDef {
  id: ChemicalDrawerTab;
  elementId: string;
  labelKey: string;
}

const TABS: ChemicalDrawerTabDef[] = [
  {id: 'overview', elementId: 'chemicalDrawerTabOverview', labelKey: 'Chemicals: Overview'},
  {id: 'placements', elementId: 'chemicalDrawerTabPlacements', labelKey: 'Chemicals: Placements'},
  {id: 'stock', elementId: 'chemicalDrawerTabStock', labelKey: 'Chemicals: Stock history'},
];

/**
 * W2 "Produktdetaljer i sidepanel": BMD overview (status dates, deadlines,
 * H-statements, SDS), the chemical's placements on the property and the
 * placement's stock history (server balances, E-06). Actions close the panel
 * with the chosen action so the page opens the matching dialog (same chaining
 * as the adhoc drawer). The tabs follow the WAI-ARIA tabs pattern (roving
 * tabindex, arrow keys, Home/End).
 */
@Component({
  selector: 'app-chemical-product-drawer',
  imports: [NgIf, NgFor, NgSwitch, NgSwitchCase, DatePipe, DecimalPipe, TranslatePipe, MatIcon, MatIconButton, MatTooltip,
    MatMenu, MatMenuItem, MatMenuTrigger, ChemicalGhsPictogramsComponent],
  templateUrl: './chemical-product-drawer.component.html',
  styleUrls: ['./chemical-product-drawer.component.scss'],
})
export class ChemicalProductDrawerComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalProductDrawerComponent, ChemicalDrawerResult>);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);
  private host = inject(ElementRef, {optional: true});
  readonly data: ChemicalProductDrawerData = inject(MAT_DIALOG_DATA);

  readonly row = this.data.row;
  readonly stockEnabled = this.data.stockEnabled;
  /** The stock tab exists only with stock on (spec §11). */
  readonly tabs: ChemicalDrawerTabDef[] = TABS.filter((t) => t.id !== 'stock' || this.stockEnabled);
  readonly placements: ChemicalPlacementModel[] = placementsOfChemical(this.data.inventory, this.row.placement.chemicalId);
  readonly stockHistory: ChemicalStockHistoryRow[] = placementStockHistory(this.data.inventory, this.row.placement.id);
  /** The eForm user's language when the panel opened (the panel is modal, so it is not followed while open). */
  private readonly uiLocale: string = currentUiLocale(this.translate);
  /** "SDS" opens the document in the user's language; old data without documents falls back to the product's sheet. */
  readonly sdsFileName = sdsFileNameForLocale(this.row, this.uiLocale);
  /** The SDS languages of the "Andre sprog" menu. */
  readonly otherSdsDocuments: ChemicalSdsDocumentModel[] = otherSdsDocuments(sdsCandidates(this.row), this.uiLocale);
  readonly labels: ChemicalLabelModel[] = labelsOf(this.row.entry);
  readonly stockUnitLabel = stockUnitLabel;
  tab: ChemicalDrawerTab = 'overview';

  get selectedTab(): ChemicalDrawerTabDef {
    return this.tabs.find((t) => t.id === this.tab);
  }

  locationName(locationId: number): string {
    return this.data.inventory.locations.find((l) => l.id === locationId)?.name ?? '';
  }

  /** Arrow keys move to the previous/next tab (wrapping), Home/End to the first/last; the new tab gets focus. */
  onTabKeydown(event: KeyboardEvent): void {
    const index = this.tabs.findIndex((t) => t.id === this.tab);
    const last = this.tabs.length - 1;
    const next: Record<string, number> = {
      ArrowRight: index === last ? 0 : index + 1,
      ArrowLeft: index === 0 ? last : index - 1,
      Home: 0,
      End: last,
    };
    if (!(event.key in next)) {
      return;
    }
    event.preventDefault();
    const target = this.tabs[next[event.key]];
    this.tab = target.id;
    // Focus follows selection; the roving tabindex catches up on the next change detection.
    this.host?.nativeElement.querySelector(`#${target.elementId}`)?.focus();
  }

  act(action: ChemicalDrawerAction): void {
    this.dialogRef.close({action, row: this.row});
  }

  close(): void {
    this.dialogRef.close(undefined);
  }

  openSds(): void {
    if (this.sdsFileName) {
      openSafetyDataSheet(this.chemicalsService, this.toastr, this.translate, this.sdsFileName);
    }
  }

  openSdsDocument(doc: ChemicalSdsDocumentModel): void {
    openSafetyDataSheet(this.chemicalsService, this.toastr, this.translate, doc.fileName);
  }

  sdsLanguageText(doc: ChemicalSdsDocumentModel): string {
    return sdsLanguageText(doc.language, this.translate);
  }

  /** The one label (the button is a plain button then). */
  openLabel(): void {
    if (this.labels.length) {
      this.openLabelFile(this.labels[0]);
    }
  }

  openLabelFile(label: ChemicalLabelModel): void {
    openChemicalLabel(this.chemicalsService, this.toastr, this.translate, label.fileName);
  }

  labelText(label: ChemicalLabelModel): string {
    return labelOptionText(label, this.translate);
  }
}
