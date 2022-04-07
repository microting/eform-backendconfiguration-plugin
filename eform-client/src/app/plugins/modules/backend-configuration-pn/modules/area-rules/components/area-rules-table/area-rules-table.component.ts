import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
} from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from '../../../../enums';
import { AreaModel, AreaRuleSimpleModel } from '../../../../models';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaRulesTableComponent implements OnChanges {
  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @Input() selectedArea: AreaModel = new AreaModel();
  @Output()
  showPlanAreaRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showEditRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showDeleteRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showEditEntityListModal: EventEmitter<number> = new EventEmitter();

  get areaRuleAlarms() {
    return AreaRuleT2AlarmsEnum;
  }

  get areaRuleTypes() {
    return AreaRuleT2TypesEnum;
  }

  tableItemsForAreaRulesDefaultT3: AreaRuleSimpleModel[] = [];

  tableHeadersT1: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT2: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Type', elementId: 'typeTableHeader', sortable: false },
    { name: 'Alarm', elementId: 'alarmTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT3: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    // {
    //   name: 'Checklist stable',
    //   elementId: 'checklistStableTableHeader',
    //   sortable: false,
    // },
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
    // { name: 'Tail bite', elementId: 'tailBiteTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT4: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT5: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Week Day', elementId: 'weekDayTableHeader', sortable: false },
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT6: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    // {
    //   name: 'Hours and energy',
    //   elementId: 'hoursAndEnergyTableHeader',
    //   sortable: false,
    // },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT7: TableHeaderElementModel[] = [
    { name: 'ID', elementId: 'ID', sortable: false},
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  constructor() {}

  ngOnChanges(changes: SimpleChanges): void {
    if (((changes.selectedArea && !changes.selectedArea.firstChange) ||
      (changes.areaRules && !changes.areaRules.firstChange))
      && this.areaRules && this.selectedArea &&
      this.selectedArea.type === 3) {
      this.tableItemsForAreaRulesDefaultT3 = this.areaRules.filter(x => x.isDefault);
      this.areaRules = this.areaRules.filter(x => !x.isDefault);
    }
  }

  onShowPlanAreaRule(rule: AreaRuleSimpleModel) {
    this.showPlanAreaRuleModal.emit(rule);
  }

  onShowEditRuleModal(rule: AreaRuleSimpleModel) {
    this.showEditRuleModal.emit(rule);
  }

  onShowDeleteRuleModal(rule: AreaRuleSimpleModel) {
    this.showDeleteRuleModal.emit(rule);
  }

  getWeekDayName(areaRule: AreaRuleSimpleModel): string {
    const weekNames = [
      { id: 1, name: 'Monday' },
      { id: 2, name: 'Tuesday' },
      { id: 3, name: 'Wednesday' },
      { id: 4, name: 'Thursday' },
      { id: 5, name: 'Friday' },
      { id: 6, name: 'Saturday' },
      { id: 0, name: 'Sunday' },
    ];
    return weekNames.find((y) => y.id === areaRule.typeSpecificFields.dayOfWeek)
      .name;
  }

  onShowEditEntityListModal(groupId?: number) {
    this.showEditEntityListModal.emit(groupId);
  }
}
