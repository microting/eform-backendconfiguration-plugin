import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
} from '@angular/core';
import {ReportEformItemModel} from '../../../models/report';
import {CaseDeleteComponent} from '../../../components';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {Subscription, take} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {format, parseISO} from 'date-fns';
import {selectAuthIsAuth, selectCurrentUserFullName} from 'src/app/state';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-report-table',
  templateUrl: './report-table.component.html',
  styleUrls: ['./report-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportTableComponent implements OnInit, OnChanges, OnDestroy {
  @Input() items: ReportEformItemModel[] = [];
  @Input() reportIndex: number;
  @Input() itemHeaders: { key: string; value: string }[] = [];
  @Input() newPostModal: any;
  @Output() planningCaseDeleted: EventEmitter<void> = new EventEmitter<void>();
  @Output() btnViewPicturesClicked: EventEmitter<{ reportIndex: number, caseId: number }> = new EventEmitter();
  @Output() editCaseClicked: EventEmitter<{ microtingSdkCaseId: number, eFormId: number, id: number }> = new EventEmitter();
  isAdmin: boolean = false;

  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'microtingSdkCaseId'},
    {header: this.translateService.stream('Property name'), field: 'propertyName'},
    {
      header: this.translateService.stream('Submitted date'),
      field: 'microtingSdkCaseDoneAt',
      type: 'date',
      typeParameter: {format: 'dd.MM.y'}
    },
    {header: this.translateService.stream('Done by'), field: 'doneBy'},
    {header: this.translateService.stream('Name'), field: 'itemName'},
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          tooltip: this.translateService.stream('View images'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onClickViewPicture(record.microtingSdkCaseId),
          icon: 'image',
          iif: (record: ReportEformItemModel) => record.imagesCount !== 0,
        },
        {
          tooltip: this.translateService.stream('Edit'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onClickEditCase(record.microtingSdkCaseId, record.eFormId, record.id),
          icon: 'edit',
        },
        {
          tooltip: this.translateService.stream('Delete'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onShowDeletePlanningCaseModal(record),
          color: 'warn',
          icon: 'delete',
        }
      ]
    },
  ];
  adminTableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'microtingSdkCaseId'},
    {header: this.translateService.stream('Planning Id'), field: 'itemId'},
    {header: this.translateService.stream('eForm Id'), field: 'eFormId'},
    {header: this.translateService.stream('Property name'), field: 'propertyName'},
    {
      header: this.translateService.stream('Submitted date'),
      field: 'microtingSdkCaseDoneAt',
      type: 'date',
      typeParameter: {format: 'dd.MM.y HH:mm'}
    },
    {header: this.translateService.stream('Server time'), field: 'serverTime', type: 'date', typeParameter: {format: 'dd.MM.y HH:mm'}},
    {header: this.translateService.stream('Done by'), field: 'doneBy'},
    {header: this.translateService.stream('Area'), field: 'itemName'},
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          tooltip: this.translateService.stream('View images'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onClickViewPicture(record.microtingSdkCaseId),
          icon: 'image',
          iif: (record: ReportEformItemModel) => record.imagesCount !== 0,
        },
        {
          tooltip: this.translateService.stream('Edit'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onClickEditCase(record.microtingSdkCaseId, record.eFormId, record.id),
          icon: 'edit',
        },
        {
          tooltip: this.translateService.stream('Delete'),
          type: 'icon',
          click: (record: ReportEformItemModel) => this.onShowDeletePlanningCaseModal(record),
          color: 'warn',
          icon: 'delete',
        }
      ]
    },
  ];
  mergedTableHeaders: MtxGridColumn[] = [];

  caseDeleteComponentComponentAfterClosedSub$: Subscription;
  public isAuth$ = this.store.select(selectAuthIsAuth);
  private selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);
  private selectCurrentUserFullName$ = this.store.select(selectCurrentUserFullName);

  constructor(
    private store: Store,
    private translateService: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {
    this.selectAuthIsAdmin$.pipe(take(1)).subscribe((selectAuthIsAdmin) => this.isAdmin = selectAuthIsAdmin);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (/*!changes.itemHeaders.isFirstChange() && */changes.itemHeaders) {
      const itemHeaders = this.itemHeaders.map((x, i): MtxGridColumn => {
        return {
          header: x.value,
          field: x.value,
          formatter: (record: ReportEformItemModel) => {
            if (record.caseFields[i].value === 'checked') {
              return `<span class="material-icons">done</span>`;
            }
            if (record.caseFields[i].value !== 'checked' && record.caseFields[i].value !== 'unchecked') {
              // @ts-ignore
              if (record.caseFields[i].key === 'number') {
                return record.caseFields[i].value.replace('.', ',');
              }
              // @ts-ignore
              if (record.caseFields[i].key === 'date') {
                if (record.caseFields[i].value === null || record.caseFields[i].value === '') {
                  return '';
                }
                // return moment(record.caseFields[i].value).format('DD.MM.YYYY');
                return format(parseISO(record.caseFields[i].value), 'dd.MM.yyyy');
              }
              // @ts-ignore
              if (record.caseFields[i].key !== 'number') {
                return record.caseFields[i].value;
              }
            }
          },
        };
      });
      if (this.isAdmin) {
        this.adminTableHeaders.find(x => x.field === 'actions').class = ((record: ReportEformItemModel, _) =>
          record ? `id-${record.id}` : '');//microtingSdkCaseId${record.microtingSdkCaseId}-eFormId${record.eFormId}-
        this.mergedTableHeaders = [...this.adminTableHeaders, ...itemHeaders];
      } else {
        this.tableHeaders.find(x => x.field === 'actions').class = ((record: ReportEformItemModel, _) =>
          record ? `id-${record.id}` : '');//microtingSdkCaseId${record.microtingSdkCaseId}-eFormId${record.eFormId}-
        this.mergedTableHeaders = [...this.tableHeaders, ...itemHeaders];
      }
    }
  }

  ngOnInit(): void {
  }

  openCreateModal(
    caseId: number,
    eformId: number,
    pdfReportAvailable: boolean
  ) {
    this.newPostModal.caseId = caseId;
    this.newPostModal.efmroId = eformId;
    this.selectCurrentUserFullName$.subscribe((selectCurrentUserFullName$) =>
      this.newPostModal.currentUserFullName = selectCurrentUserFullName$);
    this.newPostModal.pdfReportAvailable = pdfReportAvailable;
    this.newPostModal.show();
  }

  onShowDeletePlanningCaseModal(item: ReportEformItemModel) {
    this.caseDeleteComponentComponentAfterClosedSub$ = this.dialog.open(CaseDeleteComponent,
      {...dialogConfigHelper(this.overlay, item)})
      .afterClosed().subscribe(data => data ? this.onPlanningCaseDeleted() : undefined);
  }

  onPlanningCaseDeleted() {
    this.planningCaseDeleted.emit();
  }

  onClickViewPicture(caseId: number) {
    this.btnViewPicturesClicked.emit({reportIndex: this.reportIndex, caseId});
  }

  onClickEditCase(microtingSdkCaseId: number, eFormId: number, id: number) {
    this.editCaseClicked.emit({microtingSdkCaseId, eFormId, id});
  }

  ngOnDestroy(): void {
  }
}
