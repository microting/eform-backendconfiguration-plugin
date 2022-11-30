import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {TableHeaderElementModel} from 'src/app/common/models';
import {CompliancesModel} from '../../../../models';
import {PropertyCompliancesColorBadgesEnum} from '../../../../enums';
import {CompliancesStateService} from '../store';
import {AuthStateService} from 'src/app/common/store';
import {Sort} from '@angular/material/sort';
import {TranslateService} from '@ngx-translate/core';
import { MtxGridColumn } from '@ng-matero/extensions/grid';
import {ActivatedRoute, Router} from '@angular/router';

@Component({
  selector: 'app-compliances-table',
  templateUrl: './compliances-table.component.html',
  styleUrls: ['./compliances-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompliancesTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id'},
    {
      header: this.translateService.stream('Deadline'),
      field: 'deadline',
      type: 'date',
      typeParameter: {format: 'dd.MM.y'},
    },
    {header: this.translateService.stream('areas'), field: 'controlArea',},
    {header: this.translateService.stream('Task'), field: 'itemName',},
    {
      header: this.translateService.stream('Responsible'),
      field: 'responsible',
      formatter: (record: CompliancesModel) => record.responsible
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
          iif: (record: CompliancesModel) => this.canEdit(record.deadline),
          click: (record: CompliancesModel) =>
            this.router.navigate([
              '/plugins/backend-configuration-pn/compliances/case/',
              record.caseId,
              record.eformId,
              this.propertyId,
              record.deadline,
              (this.isComplianceThirtyDays === undefined ? 'false' : 'true'),
              record.id
            ], {relativeTo: this.route}),
        }
      ]
    },
  ]
  @Input() compliances: CompliancesModel[];
  @Input() propertyId: number;
  @Input() isComplianceThirtyDays: boolean;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();

  constructor(
    public compliancesStateService: CompliancesStateService,
    public authStateService: AuthStateService,
    private translateService: TranslateService,
    private router: Router,
    private route: ActivatedRoute,
  ) {
  }

  ngOnInit(): void {
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

  getResponsibles(responsibles: string[]) {
    return responsibles;
  }

  sortTable(sort: Sort) {
    this.compliancesStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  canEdit(date : Date) : boolean {
    const today = new Date();
    const deadline = new Date(date);
    const result = deadline < today;
    return result;
  }
}
