import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';
import { AreaRuleSimpleModel } from '../../../../../models';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaRulesTableComponent implements OnInit {
  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @Output() showPlanAreaRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output() showEditRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output() showDeleteRuleModal: EventEmitter<AreaRuleSimpleModel> =
    new EventEmitter();
  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Eform', elementId: 'eformTableHeader', sortable: false },
    { name: 'Language', elementId: 'languageTableHeader', sortable: false },
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
