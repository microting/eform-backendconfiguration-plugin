import {ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output,
  inject
} from '@angular/core';
import {ComplianceModel, ReportEformItemModel} from '../../../../models';
import {PropertyCompliancesColorBadgesEnum} from '../../../../enums';
import {CompliancesStateService} from '../store';
import {AuthStateService} from 'src/app/common/store';
import {Sort} from '@angular/material/sort';
import {TranslateService} from '@ngx-translate/core';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {ActivatedRoute, Router} from '@angular/router';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {
  ComplianceDeleteComponent
} from '../compliance-delete/compliance-delete.component';
import {Subscription, take} from 'rxjs';
import {selectAuthIsAdmin, selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {isFutureTask} from '../../../../helpers';

@Component({
    selector: 'app-compliances-table',
    templateUrl: './compliances-table.component.html',
    styleUrls: ['./compliances-table.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    standalone: false
})
export class CompliancesTableComponent implements OnInit {
  private store = inject(Store);
  public compliancesStateService = inject(CompliancesStateService);
  public authStateService = inject(AuthStateService);
  private translateService = inject(TranslateService);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private route = inject(ActivatedRoute);

  mergedTableHeaders: MtxGridColumn[] = [];
  @Output() complianceDeleted: EventEmitter<void> = new EventEmitter<void>();

  complianceDeleteComponentAfterClosedSub$: Subscription;
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id'},
    {
      header: this.translateService.stream('Deadline'),
      field: 'deadline',
      type: 'date',
      typeParameter: {format: 'dd.MM.y'},
    },
    {header: this.translateService.stream('areas'), field: 'controlArea'},
    {header: this.translateService.stream('Task'), field: 'itemName'},
    {
      header: this.translateService.stream('Responsible'),
      field: 'responsible',
      formatter: (record: ComplianceModel) => record.responsible
        .map(responsible => `<span>${responsible.value}<small class="text-accent"> (${responsible.key})</small></span><br/>`)
        .toString()
        // @ts-ignore
        .replaceAll(',', ''),
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          type: 'icon',
          tooltip:  this.translateService.stream('Edit Case'),
          icon: 'edit',
          iif: (record: ComplianceModel) => this.canEdit(record.complianceDeadline),
          click: (record: ComplianceModel) =>
            this.router.navigate([
              '/plugins/backend-configuration-pn/compliances/case/'],
              {
                relativeTo: this.route,
                queryParams: {
                  sdkCaseId: record.caseId,
                  templateId: record.eformId,
                  propertyId: this.propertyId,
                  deadline: record.deadline,
                  thirtyDays: (this.isComplianceThirtyDays === undefined ? 'false' : 'true'),
                  complianceId: record.id,
                  reverseRoute: `/plugins/backend-configuration-pn/compliances/${this.propertyId}`
                }}),
        }
      ]
    },
  ];
  adminTableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id'},
    {header: this.translateService.stream('CreatedAt'),
      field: 'createdAt',
      type: 'date',
      typeParameter: {format: 'dd.MM.y HH:mm'},
    },
    {
      header: this.translateService.stream('Deadline'),
      field: 'deadline',
      type: 'date',
      typeParameter: {format: 'dd.MM.y'},
    },
    {header: this.translateService.stream('areas'), field: 'controlArea'},
    {header: this.translateService.stream('Task'), field: 'itemName'},
    {
      header: this.translateService.stream('Responsible'),
      field: 'responsible',
      formatter: (record: ComplianceModel) => record.responsible
        .map(responsible => `<span>${responsible.value}<small class="text-accent"> (${responsible.key})</small></span><br/>`)
        .toString()
        // @ts-ignore
        .replaceAll(',', ''),
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          type: 'icon',
          tooltip:  this.translateService.stream('Edit Case'),
          icon: 'edit',
          iif: (record: ComplianceModel) => this.canEdit(record.complianceDeadline),
          click: (record: ComplianceModel) =>
            this.router.navigate([
              '/plugins/backend-configuration-pn/compliances/case/'+record.caseId +'/'+ record.eformId+'/'+ this.propertyId+'/'+ record.deadline.toISOString()+'/'+false+'/'+ record.id,
            ], {relativeTo: this.route, queryParams: {reverseRoute: `/plugins/backend-configuration-pn/compliances/${this.propertyId}`}}),
        },
        {
          type: 'icon',
          tooltip:  this.translateService.stream('Delete Case'),
          icon: 'delete',
          color: 'warn',
          // #1300: every row here is an uncompleted occurrence, so a future one
          // gets no delete action (the server refuses it as well).
          iif: (record: ComplianceModel) => this.canDelete(record.complianceDeadline),
          click: (record: ReportEformItemModel) => this.onShowDeleteComplianceModal(record),
        }
      ]
    },
  ];
  @Input() complianceList: ComplianceModel[];
  @Input() propertyId: number;
  @Input() isComplianceThirtyDays: boolean;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  public isAuth$ = this.store.select(selectAuthIsAuth);
  private selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);

  

  ngOnInit(): void {
    // Only an admin gets the delete action (and the CreatedAt column).
    this.selectAuthIsAdmin$.pipe(take(1)).subscribe((isAdmin) =>
      this.mergedTableHeaders = isAdmin ? this.adminTableHeaders : this.tableHeaders);
  }

  getColorBadge(compliance: PropertyCompliancesColorBadgesEnum): string {
    switch (compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'primary';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'accent';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'warn';
      default:
        return 'primary';
    }
    /*switch (compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'bg-success';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'bg-danger';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'bg-warning';
      default:
        return 'bg-success';
    }*/
  }

  getResponsibles(responsible: string[]) {
    return responsible;
  }

  sortTable(sort: Sort) {
    this.compliancesStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  /**
   * #1300: date-level, not a timestamp comparison — a task can be filled in
   * from its own day onward, never before (Copenhagen date). `date` is the
   * compliance's own stored deadline — the date the server's guard judges —
   * not the displayed one, which is a day earlier for legacy rows (#1382).
   */
  canEdit(date: Date): boolean {
    return !isFutureTask(date);
  }

  /** #1300: an uncompleted future task cannot be deleted. Same date rule as `canEdit`. */
  canDelete(date: Date): boolean {
    return !isFutureTask(date);
  }

  onShowDeleteComplianceModal(item: ReportEformItemModel) {
    // The grid hands over the row, a ComplianceModel, despite the declared type.
    const complianceDeadline = (item as unknown as ComplianceModel)?.complianceDeadline;
    if (complianceDeadline && !this.canDelete(complianceDeadline)) {
      return;
    }
    this.complianceDeleteComponentAfterClosedSub$ = this.dialog.open(ComplianceDeleteComponent,
      {...dialogConfigHelper(this.overlay, item)})
      .afterClosed().subscribe(data => data ? this.onComplianceDeleted() : undefined);
  }

  onComplianceDeleted() {
    this.complianceDeleted.emit();
  }

}
