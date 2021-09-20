import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import { AreaModel, AreaRuleSimpleModel } from '../../../../../models';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaRulesTableComponent implements OnInit {
  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @Input() selectedArea: AreaModel = new AreaModel();
  @Output() showPlanAreaRuleModal: EventEmitter<AreaRuleSimpleModel> =
    new EventEmitter();
  @Output() showEditRuleModal: EventEmitter<AreaRuleSimpleModel> =
    new EventEmitter();
  @Output() showDeleteRuleModal: EventEmitter<AreaRuleSimpleModel> =
    new EventEmitter();

  get areaRuleAlarms() {
    return AreaRuleT2AlarmsEnum;
  }

  get areaRuleTypes() {
    return AreaRuleT2TypesEnum;
  }

  tableHeadersT1: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Eform', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT2: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Type', elementId: 'typeTableHeader', sortable: false },
    { name: 'Alarm', elementId: 'alarmTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT3: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    {
      name: 'Checklist stable',
      elementId: 'checklistStableTableHeader',
      sortable: false,
    },
    { name: 'Eform', elementId: 'eformTableHeader', sortable: false },
    { name: 'Tail bite', elementId: 'tailBiteTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT4: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT5: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Week Day', elementId: 'weekDayTableHeader', sortable: false },
    { name: 'Eform', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  constructor() {}

  ngOnInit(): void {}

  onShowPlanAreaRule(rule: AreaRuleSimpleModel) {
    this.showPlanAreaRuleModal.emit(rule);
  }

  onShowEditRuleModal(rule: AreaRuleSimpleModel) {
    this.showEditRuleModal.emit(rule);
  }

  onShowDeleteRuleModal(rule: AreaRuleSimpleModel) {
    this.showDeleteRuleModal.emit(rule);
  }
}
