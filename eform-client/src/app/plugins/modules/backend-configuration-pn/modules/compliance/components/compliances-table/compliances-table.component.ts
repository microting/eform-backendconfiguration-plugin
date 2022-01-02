import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';
import { CompliancesModel } from '../../../../models';
import { PropertyCompliancesColorBadgesEnum } from '../../../../enums';
import * as R from 'ramda';
import { CompliancesStateService } from '../store';

@Component({
  selector: 'app-compliances-table',
  templateUrl: './compliances-table.component.html',
  styleUrls: ['./compliances-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompliancesTableComponent implements OnInit {
  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Id', sortable: false },
    { name: 'ControlArea', visibleName: 'areas', sortable: false },
    { name: 'Deadline', sortable: false },
    { name: 'Task', sortable: false },
    { name: 'Responsible', visibleName: 'Assigned to', sortable: false },
    // { name: 'Compliance', sortable: true },
    // { name: 'Actions', elementId: '', sortable: false },
  ];
  @Input() compliances: CompliancesModel[];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();

  constructor(public compliancesStateService: CompliancesStateService) {}

  ngOnInit(): void {}

  getColorBadge(compliance: PropertyCompliancesColorBadgesEnum): string {
    switch (compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'bg-success';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'bg-danger';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'bg-warning';
      default:
        return 'bg-success';
    }
  }

  getResponsibles(responsibles: string[]) {
    debugger;
    return responsibles;
  }

  sortTable(sort: string) {
    this.compliancesStateService.onSortTable(sort);
    this.updateTable.emit();
  }
}
