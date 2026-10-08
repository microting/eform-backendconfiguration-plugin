import {Overlay} from '@angular/cdk/overlay';
import {NgFor} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MatDialog} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatTooltipModule} from '@angular/material/tooltip';
import {ActivatedRoute} from '@angular/router';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {Subscription, finalize, switchMap, tap} from 'rxjs';
import {TailBiteActionType} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {openConfirm} from '../../shared/tail-bite-confirm';
import {propertyIdParam} from '../../shared/tail-bite-route';
import {askText} from '../tail-bite-text-dialog/tail-bite-text-dialog.component';

/** The actions workers tick when they register bites. Names are unique per property (the server checks). */
@Component({
  selector: 'app-tail-bite-action-types-page',
  templateUrl: './tail-bite-action-types-page.component.html',
  imports: [NgFor, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatTooltipModule,
    TranslateModule],
})
export class TailBiteActionTypesPageComponent implements OnInit, OnDestroy {
  private service = inject(BackendConfigurationPnTailBiteService);
  private route = inject(ActivatedRoute);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);

  actionTypes: TailBiteActionType[] = [];
  newName = '';
  busy = false;
  private propertyId: number | null = null;
  private sub?: Subscription;
  private request?: Subscription;
  private requestSeq = 0;

  ngOnInit(): void {
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
    this.actionTypes = [];
    this.newName = '';
  }

  /** Refreshes the list. A refused or failed refresh keeps what is shown; only the first load starts (and stays) empty. */
  load(): void {
    const propertyId = this.propertyId;
    if (propertyId === null) {
      return;
    }
    const seq = ++this.requestSeq;
    this.request?.unsubscribe();
    this.request = this.service.getActionTypes(propertyId).subscribe({
      next: (res) => {
        if (seq === this.requestSeq && res?.success) {
          this.actionTypes = res.model;
        }
      },
      error: () => undefined,
    });
  }

  /** Adds the typed name. The text stays in the field unless the server accepted it; the list reloads whatever the outcome. */
  add(): void {
    const propertyId = this.propertyId;
    const name = this.newName.trim();
    if (!name || propertyId === null || this.busy) {
      return;
    }
    this.busy = true;
    this.service.createActionType(propertyId, name).pipe(finalize(() => (this.busy = false))).subscribe({
      next: (res) => {
        if (res?.success && propertyId === this.propertyId) {
          this.newName = '';
        }
        this.load();
      },
      error: () => this.load(),
    });
  }

  rename(a: TailBiteActionType): void {
    if (this.busy) {
      return;
    }
    this.busy = true;
    askText(this.dialog, this.overlay, {title: this.translate.instant('Rename action type'), label: 'Name', value: a.name})
      .pipe(switchMap((name) => this.service.renameActionType(a.id, name)), finalize(() => (this.busy = false)))
      .subscribe({next: () => this.load(), error: () => this.load()});
  }

  remove(a: TailBiteActionType): void {
    if (this.busy) {
      return;
    }
    openConfirm(this.dialog, this.overlay, {
      headerText: this.translate.instant('Delete action type'), itemLabel: this.translate.instant('Name'), itemName: a.name,
      confirmText: this.translate.instant('Delete'), confirmId: 'tailBiteActionTypeDeleteConfirm',
    }, () => {
      this.busy = true;
      return this.service.deleteActionType(a.id).pipe(
        tap({
          next: (res) => {
            if (!res?.success) {
              this.load();
            }
          },
          error: () => this.load(),
        }),
        finalize(() => (this.busy = false)),
      );
    // tap reloads on a refusal or an error and leaves the dialog open; the error is swallowed so it is not unhandled.
    }).subscribe({next: () => this.load(), error: () => undefined});
  }
}
