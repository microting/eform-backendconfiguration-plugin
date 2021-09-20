import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {
  AreaModel,
  AreaRuleAssignedSitesModel,
  AreaRulePlanningModel,
  AreaRuleSimpleModel,
} from '../../../../models';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @ViewChild('frame', { static: false }) frame;
  @Output() updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> =
    new EventEmitter<AreaRulePlanningModel>();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();
  selectedAreaRule: AreaRuleSimpleModel = new AreaRuleSimpleModel();

  get currentDate() {
    return new Date();
  }

  get currentDatePlusTwoWeeks() {
    return new Date(+new Date() + 1209600000);
  }

  constructor() {}

  ngOnInit() {}

  show(planning: AreaRulePlanningModel, rule: AreaRuleSimpleModel) {
    this.selectedAreaRulePlanning = planning
      ? planning
      : new AreaRulePlanningModel();
    this.selectedAreaRule = rule;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  onUpdateAreaRulePlan() {
    this.updateAreaRulePlan.emit(this.selectedAreaRulePlanning);
  }

  addToArray(e: any, siteId: number) {
    const assignmentObject = new AreaRuleAssignedSitesModel();
    if (e.target.checked) {
      assignmentObject.checked = true;
      assignmentObject.siteId = siteId;
      this.selectedAreaRulePlanning.assignedSites = [
        ...this.selectedAreaRulePlanning.assignedSites,
        assignmentObject,
      ];
    } else {
      this.selectedAreaRulePlanning.assignedSites =
        this.selectedAreaRulePlanning.assignedSites.filter(
          (x) => x.siteId !== siteId
        );
    }
  }

  getAssignmentBySiteId(siteId: number) {
    return (
      this.selectedAreaRulePlanning.assignedSites.find(
        (x) => x.siteId === siteId
      ) ?? {
        siteId,
        isChecked: false,
      }
    );
  }
}
