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
import {
  AreaModel,
  AreaRuleSimpleModel,
  AreaRuleT5Model,
} from '../../../../models';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaRulesTableComponent implements OnInit {
  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @Input() selectedArea: AreaModel = new AreaModel();
  @Output()
  showPlanAreaRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showEditRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showDeleteRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();

  get areaRuleAlarms() {
    return AreaRuleT2AlarmsEnum;
  }

  get areaRuleTypes() {
    return AreaRuleT2TypesEnum;
  }

  tableHeadersT1: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
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
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
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
    { name: 'eForm', elementId: 'eformTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  tableHeadersT6: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    // {
    //   name: 'Hours and energy',
    //   elementId: 'hoursAndEnergyTableHeader',
    //   sortable: false,
    // },
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
    return weekNames.find((y) => {
      areaRule.typeSpecificFields = areaRule.typeSpecificFields as AreaRuleT5Model;
      return y.id === areaRule.typeSpecificFields.dayOfWeek;
    }).name;
  }
}
