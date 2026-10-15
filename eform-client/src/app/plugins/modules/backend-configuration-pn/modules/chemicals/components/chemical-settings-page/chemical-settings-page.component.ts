import {Overlay} from '@angular/cdk/overlay';
import {Component, DestroyRef, HostListener, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {MatDialog} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {Observable} from 'rxjs';
import {finalize} from 'rxjs/operators';
import {ChemicalDigestItemModel, ChemicalDigestModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {selectChemicalsPropertyId} from '../../../../state';
import {ChemicalUnsavedChangesAware, confirmDiscardWhenDirty, warnBeforeUnload} from '../../chemicals-unsaved-changes';
import {addRecipients, confirmDiscard} from '../../helpers';

/** The fixed alert rules (spec section 9; shown read-only, "Fast for alle kunder"). */
export interface ChemicalAlertThresholdRow {
  key: string;
  hintKey?: string;
}

export const CHEMICAL_ALERT_THRESHOLDS: readonly ChemicalAlertThresholdRow[] = [
  {key: 'Chemicals: Threshold six months'},
  {key: 'Chemicals: Threshold one month'},
  {key: 'Chemicals: Threshold passed', hintKey: 'Chemicals: Threshold passed hint'},
  {key: 'Chemicals: Threshold status changed', hintKey: 'Chemicals: Threshold status changed hint'},
];

/** One section of the digest preview, in the order and under the headings of the e-mail (Plan D C5). */
export interface ChemicalDigestPreviewSection {
  id: 'passed' | 'upcoming' | 'status' | 'new';
  key: string;
  items: ChemicalDigestItemModel[];
  /** Show the deadline date next to the product. */
  deadline: boolean;
  /** Show the registration date and who registered (instead of the status text). */
  registration: boolean;
}

/**
 * W5: stock toggle, weekly digest recipients (e-mail chips), the read-only alert
 * thresholds and a preview of the NEXT digest built by the server from the same rule
 * the digest job uses (Plan E decision 3; evaluated on the e-mail's Monday 07:00 slot,
 * the week the heading names). Edits are kept until Save (one PUT); unsaved edits are
 * never dropped silently: leaving the screen, switching property (the container asks
 * through {@link confirmDiscardChanges}) and reloading all ask first. The first-user
 * gate is the container's.
 */
@Component({
  selector: 'app-chemical-settings-page',
  templateUrl: './chemical-settings-page.component.html',
  styleUrls: ['./chemical-settings-page.component.scss'],
  standalone: false,
})
export class ChemicalSettingsPageComponent implements OnInit, ChemicalUnsavedChangesAware {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);

  readonly thresholds = CHEMICAL_ALERT_THRESHOLDS;
  /** Null when the customer has no property: the container explains that, the page stays empty. */
  propertyId: number | null = null;
  stockEnabled = false;
  recipients: string[] = [];
  recipientInput = '';
  invalidRecipients: string[] = [];
  preview: ChemicalDigestModel | null = null;
  previewSections: ChemicalDigestPreviewSection[] = [];
  /** The preview could not be read (refused or HTTP error); the card says so instead of staying empty. */
  previewFailed = false;
  /** The stored settings are being read: the form is not offered until they arrive. */
  loading = false;
  /** Reading the stored settings failed (refused or HTTP error): no form, so nothing can overwrite them from defaults. */
  loadFailed = false;
  /** A save is in flight and cannot be cancelled: the container locks the property picker. */
  busy = false;
  private loadedStock = false;
  private loadedRecipients: string[] = [];
  /** Bumped by every load / preview request; an answer from an older one is ignored (A to B to A switches). */
  private loadGeneration = 0;
  private previewGeneration = 0;
  /** The last save was refused or failed; the edits are kept for a retry. */
  saveFailed = false;

  ngOnInit(): void {
    this.store.select(selectChemicalsPropertyId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((id) => {
        this.propertyId = id;
        this.load();
      });
  }

  /** The form shows the stored settings and may be edited and saved. */
  get ready(): boolean {
    return this.propertyId !== null && !this.loading && !this.loadFailed;
  }

  /**
   * Differs from the loaded settings (so undoing an edit makes the page clean again). An address typed
   * but not yet added is an unsaved draft too, so the discard and unload guards cover it.
   */
  get dirty(): boolean {
    return this.stockEnabled !== this.loadedStock
      || this.recipientInput.trim().length > 0
      || this.recipients.length !== this.loadedRecipients.length
      || this.recipients.some((r, i) => r !== this.loadedRecipients[i]);
  }

  onStockToggle(enabled: boolean): void {
    if (this.ready) {
      this.stockEnabled = enabled;
    }
  }

  addRecipientInput(): void {
    if (!this.ready) {
      return;
    }
    const result = addRecipients(this.recipients, this.recipientInput);
    this.recipients = result.recipients;
    this.invalidRecipients = result.invalid;
    this.recipientInput = result.invalid.join(' ');
  }

  removeRecipient(email: string): void {
    if (this.ready) {
      this.recipients = this.recipients.filter((r) => r !== email);
    }
  }

  save(): void {
    const propertyId = this.propertyId;
    if (propertyId === null || !this.ready || this.busy) {
      return;
    }
    this.busy = true;
    this.saveFailed = false;
    this.chemicalsService.saveSettings(propertyId, {stockEnabled: this.stockEnabled, digestRecipients: this.recipients})
      .pipe(finalize(() => (this.busy = false)))
      .subscribe({
        next: (res) => {
          if (propertyId !== this.propertyId) {
            return;
          }
          if (res && res.success) {
            this.apply(res.model.stockEnabled, res.model.digestRecipients);
            this.loadPreview(propertyId);
          } else {
            // The host toasts the message too; the edits stay for a retry.
            this.saveFailed = true;
          }
        },
        error: () => {
          if (propertyId === this.propertyId) {
            this.saveFailed = true;
          }
        },
      });
  }

  /** Used by the route guard and the container's property switch. */
  confirmDiscardChanges(): Observable<boolean> {
    return confirmDiscardWhenDirty(this.busy, this.dirty, () => confirmDiscard(this.dialog, this.overlay, {
      headerText: this.t('Chemicals: Discard unsaved settings?'),
      fields: this.preview ? [{header: this.t('Property'), text: this.preview.propertyName}] : [],
      discardText: this.t('Chemicals: Discard changes'),
      keepText: this.t('Chemicals: Keep editing'),
      confirmId: 'chemicalSettingsDiscardConfirm',
    }));
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    warnBeforeUnload(event, this.dirty);
  }

  private t(key: string): string {
    return this.translate.instant(key);
  }

  private load(): void {
    this.loadGeneration++;
    this.previewGeneration++;
    this.apply(false, []);
    this.setPreview(null);
    this.previewFailed = false;
    this.saveFailed = false;
    this.loadFailed = false;
    this.loading = this.propertyId !== null;
    if (this.propertyId === null) {
      return;
    }
    const propertyId = this.propertyId;
    const generation = ++this.loadGeneration;
    this.chemicalsService.getSettings(propertyId).subscribe({
      next: (res) => {
        if (generation !== this.loadGeneration) {
          return;
        }
        this.loading = false;
        if (res && res.success) {
          this.apply(res.model.stockEnabled, res.model.digestRecipients);
        } else {
          this.loadFailed = true;
        }
      },
      error: () => {
        if (generation === this.loadGeneration) {
          this.loading = false;
          this.loadFailed = true;
        }
      },
    });
    this.loadPreview(propertyId);
  }

  private loadPreview(propertyId: number): void {
    const generation = ++this.previewGeneration;
    this.chemicalsService.getDigestPreview(propertyId).subscribe({
      next: (res) => {
        if (generation === this.previewGeneration) {
          this.setPreview(res && res.success ? res.model : null);
          this.previewFailed = !(res && res.success);
        }
      },
      error: () => {
        if (generation === this.previewGeneration) {
          this.setPreview(null);
          this.previewFailed = true;
        }
      },
    });
  }

  private setPreview(preview: ChemicalDigestModel | null): void {
    this.preview = preview;
    if (!preview) {
      this.previewSections = [];
      return;
    }
    const sections: ChemicalDigestPreviewSection[] = [
      {id: 'passed', key: 'Chemicals: Digest passed', items: preview.passedDeadlines, deadline: true, registration: false},
      {id: 'upcoming', key: 'Chemicals: Digest upcoming', items: preview.upcomingDeadlines, deadline: true, registration: false},
      {id: 'status', key: 'Chemicals: Digest deregistered or expired', items: preview.deregisteredOrExpired, deadline: false, registration: false},
      {id: 'new', key: 'Chemicals: Digest new registrations', items: preview.newRegistrations, deadline: false, registration: true},
    ];
    this.previewSections = sections.filter((section) => section.items.length > 0);
  }

  private apply(stockEnabled: boolean, recipients: string[]): void {
    this.stockEnabled = stockEnabled;
    this.recipients = [...(recipients ?? [])];
    this.invalidRecipients = [];
    this.recipientInput = '';
    this.loadedStock = stockEnabled;
    this.loadedRecipients = [...this.recipients];
  }
}
