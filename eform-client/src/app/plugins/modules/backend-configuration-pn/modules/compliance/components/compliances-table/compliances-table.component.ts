import {ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output,} from '@angular/core';
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
import {Subscription} from 'rxjs';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@Component({
  selector: 'app-compliances-table',
  templateUrl: './compliances-table.component.html',
  styleUrls: ['./compliances-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompliancesTableComponent implements OnInit {
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
          iif: (record: ComplianceModel) => this.canEdit(record.deadline),
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
          iif: (record: ComplianceModel) => this.canEdit(record.deadline),
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

  constructor(
    private store: Store,
    public compliancesStateService: CompliancesStateService,
    public authStateService: AuthStateService,
    private translateService: TranslateService,
    private router: Router,
    private dialog: MatDialog,
    private overlay: Overlay,
    private route: ActivatedRoute,
  ) {
  }

  ngOnInit(): void {
    if (this.authStateService.isAdmin) {
      this.mergedTableHeaders = this.adminTableHeaders;
    } else {
      this.mergedTableHeaders = this.tableHeaders;
    }
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

  canEdit(date: Date): boolean {
    const today = new Date();
    const deadline = new Date(date);
    return deadline < today;
  }

  onShowDeleteComplianceModal(item: ReportEformItemModel) {
    this.complianceDeleteComponentAfterClosedSub$ = this.dialog.open(ComplianceDeleteComponent,
      {...dialogConfigHelper(this.overlay, item)})
      .afterClosed().subscribe(data => data ? this.onComplianceDeleted() : undefined);
  }

  onComplianceDeleted() {
    this.complianceDeleted.emit();
  }

}
