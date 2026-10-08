import {Overlay} from '@angular/cdk/overlay';
import {NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatDialog} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatTableModule} from '@angular/material/table';
import {ActivatedRoute} from '@angular/router';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {Observable, Subscription, finalize, forkJoin, tap} from 'rxjs';
import {OperationResult} from 'src/app/common/models';
import {TailBiteLocationTree} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {openConfirm, whenRefused} from '../../shared/tail-bite-confirm';
import {occupancyValidFrom, toDateOnly} from '../../shared/tail-bite-dates';
import {propertyIdParam} from '../../shared/tail-bite-route';
import {describeRule} from '../../shared/tail-bite-rule-text';
import {TailBiteLocationOption, TailBiteTreeRow, buildTreeRows, liveNodes, moveTargets, pathOptions} from '../../shared/tail-bite-tree';
import {memoize} from '../../shared/tail-bite-memo';
import {askText} from '../tail-bite-text-dialog/tail-bite-text-dialog.component';
import {buildQrSheetPdf, qrLabels} from './tail-bite-qr-pdf';

/** The server's limit for one pen range (TailBiteSetupService.MaxPenRange). */
const MAX_PEN_RANGE = 500;

/**
 * "Lokationer og QR": the property's location tree with pig counts and the rule each location follows;
 * add, rename, move and delete locations, create a pen range, set a pig count, and print QR labels.
 */
@Component({
  selector: 'app-tail-bite-locations-page',
  templateUrl: './tail-bite-locations-page.component.html',
  imports: [NgIf, FormsModule, MatCardModule, MatCheckboxModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTableModule, MtxSelectModule, TranslateModule],
})
export class TailBiteLocationsPageComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private route = inject(ActivatedRoute);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);
  private toastr = inject(ToastrService);

  readonly columns = ['check', 'name', 'pigs', 'rule'];
  readonly today = new Date();
  tree: TailBiteLocationTree | null = null;
  rows: TailBiteTreeRow[] = [];
  selectedId: number | null = null;
  checked = new Set<number>();
  penPrefix = '';
  penFrom: number | null = null;
  penTo: number | null = null;
  pigCount: number | null = null;
  validFrom: Date = new Date();
  moveTarget: number | null = null;
  busy = false;
  private propertyId: number | null = null;
  private sub?: Subscription;
  private request?: Subscription;
  private requestSeq = 0;

  ngOnInit(): void {
    this.penPrefix = this.translate.instant('Pen');
    this.sub = propertyIdParam(this.route).subscribe((id) => {
      this.propertyId = id;
      this.clearPage();
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.request?.unsubscribe();
  }

  /** Forgets everything shown for the previous property so nothing stale is visible while the next one loads. */
  private clearPage(): void {
    this.tree = null;
    this.rows = [];
    this.selectedId = null;
    this.checked = new Set();
    this.penFrom = null;
    this.penTo = null;
    this.pigCount = null;
    this.moveTarget = null;
    this.validFrom = new Date();
  }

  load(): void {
    const propertyId = this.propertyId;
    if (propertyId === null) {
      return;
    }
    const seq = ++this.requestSeq;
    this.request?.unsubscribe();
    this.request = forkJoin({
      tree: this.service.getTree(propertyId),
      rules: this.service.getRules(propertyId),
      occupancy: this.service.getOccupancy(propertyId),
    }).subscribe({
      next: ({tree, rules, occupancy}) => {
        // A refused or failed refresh keeps what is shown (selection and checks included); only a first load starts empty.
        if (seq === this.requestSeq && tree?.success && rules?.success && occupancy?.success) {
          this.tree = tree.model;
          this.rows = buildTreeRows(this.tree, rules.model, occupancy.model);
          const ids = new Set(this.rows.map((r) => r.node.id));
          this.checked = new Set([...this.checked].filter((id) => ids.has(id)));
          const keep = this.rows.find((r) => r.node.id === this.selectedId) ?? this.rows[0];
          if (keep) {
            this.select(keep);
          } else {
            this.selectedId = null;
          }
        }
      },
      // The API service already toasts the error.
      error: () => undefined,
    });
  }

  get selected(): TailBiteTreeRow | null {
    return this.rows.find((r) => r.node.id === this.selectedId) ?? null;
  }

  select(row: TailBiteTreeRow): void {
    this.selectedId = row.node.id;
    this.pigCount = row.pigsSource === 'own' ? row.pigs : null;
    this.validFrom = new Date();
    this.moveTarget = null;
  }

  ruleLabel(row: TailBiteTreeRow): string {
    if (row.ownRule) {
      return this.translate.instant('Own rule: {{rule}}', {rule: describeRule(this.translate, row.ownRule)});
    }
    return row.governingRuleLocation
      ? this.translate.instant('Inherited from {{location}}', {location: row.governingRuleLocation.name})
      : '–';
  }

  // ---------- QR selection and printing ----------

  isChecked(id: number): boolean {
    return this.checked.has(id);
  }

  setChecked(id: number, on: boolean): void {
    const next = new Set(this.checked);
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.checked = next;
  }

  get allChecked(): boolean {
    return this.rows.length > 0 && this.rows.every((r) => this.checked.has(r.node.id));
  }

  setAllChecked(on: boolean): void {
    this.checked = on ? new Set(this.rows.map((r) => r.node.id)) : new Set();
  }

  async printQr(): Promise<void> {
    const selected = this.rows.filter((r) => this.checked.has(r.node.id) && !r.node.removed).map((r) => r.node);
    if (this.busy || !selected.length || !this.tree) {
      return;
    }
    this.busy = true;
    try {
      // The root location carries the property's name (it is created from it when tail biting is enabled).
      const root = this.rows[0]?.node.name ?? '';
      const header = `${root}. ${this.translate.instant(
        'Cut along the lines. Print on waterproof label paper so the codes survive washing.')}`;
      const bytes = await buildQrSheetPdf(qrLabels(selected, liveNodes(this.tree)), header);
      const url = URL.createObjectURL(new Blob([bytes as BlobPart], {type: 'application/pdf'}));
      const link = document.createElement('a');
      link.href = url;
      link.download = `halebid-qr-${toDateOnly(new Date())}.pdf`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 10000);
    } catch {
      this.toastr.error(this.translate.instant('Could not create the QR sheet'));
    } finally {
      this.busy = false;
    }
  }

  // ---------- editing the tree ----------

  /** Labelled with full paths: a "Sektion 1" may exist under several parents. */
  get moveOptions(): TailBiteLocationOption[] {
    return this.moveOptionsOf(this.tree, this.selectedId);
  }

  // Bound to an mtx-select: keeps its reference until the tree or the selection changes (see memoize).
  private readonly moveOptionsOf = memoize((tree: TailBiteLocationTree | null, selectedId: number | null) => {
    if (selectedId === null) {
      return [];
    }
    const nodes = liveNodes(tree);
    return pathOptions(moveTargets(nodes, selectedId), nodes);
  });

  addChild(): void {
    const row = this.selected;
    if (row) {
      this.askName(this.translate.instant('Add location under {{location}}', {location: row.node.name}), '',
        (name) => this.service.createLocation(row.node.id, name));
    }
  }

  rename(): void {
    const row = this.selected;
    if (row) {
      this.askName(this.translate.instant('Rename location'), row.node.name, (name) => this.service.renameLocation(row.node.id, name));
    }
  }

  move(newParentId: number | null): void {
    const row = this.selected;
    this.moveTarget = null;
    if (row && newParentId !== null) {
      this.run(this.service.moveLocation(row.node.id, newParentId));
    }
  }

  remove(): void {
    const row = this.selected;
    const parentId = row?.node.parentId ?? null;
    if (!this.busy && row && parentId !== null) {
      openConfirm(this.dialog, this.overlay, {
        headerText: this.translate.instant('Delete {{location}} and everything under it?', {location: row.node.name}),
        itemLabel: this.translate.instant('Location'), itemName: row.node.name,
        confirmText: this.translate.instant('Delete location'), confirmId: 'tailBiteDeleteLocationConfirm',
      }, () => {
        this.busy = true;
        return this.service.deleteLocation(row.node.id).pipe(
          tap({
            next: (res) => {
              if (res?.success) {
                this.selectedId = parentId;
              } else {
                this.load();
              }
            },
            error: () => this.load(),
          }),
          finalize(() => (this.busy = false)),
        );
      // tap already reloads on a refusal or an error; the error is swallowed here so it is not unhandled.
      }).subscribe({next: () => this.load(), error: () => undefined});
    }
  }

  get penCount(): number {
    const {penFrom: from, penTo: to} = this;
    return from !== null && to !== null && Number.isInteger(from) && Number.isInteger(to) && from >= 0 && to >= from ? to - from + 1 : 0;
  }

  get penRangeValid(): boolean {
    return this.penPrefix.trim().length > 0 && this.penCount > 0 && this.penCount <= MAX_PEN_RANGE;
  }

  createPens(): void {
    const row = this.selected;
    if (row && this.penRangeValid) {
      this.run(this.service.createPenRange(row.node.id, this.penPrefix.trim(), this.penFrom!, this.penTo!));
    }
  }

  get pigCountValid(): boolean {
    return this.pigCount !== null && Number.isInteger(this.pigCount) && this.pigCount >= 0 && !!this.validFrom
      && toDateOnly(this.validFrom) <= toDateOnly(this.today);
  }

  savePigs(): void {
    const row = this.selected;
    if (row && this.pigCountValid) {
      this.run(this.service.setOccupancy(row.node.id, this.pigCount!, occupancyValidFrom(this.validFrom)));
    }
  }

  private askName(title: string, value: string, call: (name: string) => Observable<OperationResult>): void {
    if (this.busy) {
      return;
    }
    // The dialog keeps the page busy and stays open with the typed name until the server accepts it.
    this.busy = true;
    askText(this.dialog, this.overlay, {title, label: 'Name', value}, (name) => call(name).pipe(whenRefused(() => this.load())))
      .pipe(finalize(() => (this.busy = false)))
      .subscribe(() => this.load());
  }

  /** Runs one server call at a time and reloads afterwards whatever the outcome, so a conflict never leaves stale data. */
  private run(call: Observable<OperationResult>): void {
    if (this.busy) {
      return;
    }
    this.busy = true;
    call.pipe(finalize(() => (this.busy = false))).subscribe({next: () => this.load(), error: () => this.load()});
  }
}
