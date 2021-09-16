import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {
  AreaRuleModel,
  AreaRulePlanningModel,
  AreaRuleSimpleModel,
} from '../../../../../models';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> =
    new EventEmitter<AreaRulePlanningModel>();
  selectedAreaRule: AreaRuleModel = new AreaRuleModel();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();

  constructor() {}

  ngOnInit() {}

  show(rule: AreaRuleModel) {
    this.selectedAreaRule = rule;
    this.selectedAreaRulePlanning = rule.planning;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  onUpdateAreaRulePlan() {
    this.updateAreaRulePlan.emit(this.selectedAreaRulePlanning);
  }
}
