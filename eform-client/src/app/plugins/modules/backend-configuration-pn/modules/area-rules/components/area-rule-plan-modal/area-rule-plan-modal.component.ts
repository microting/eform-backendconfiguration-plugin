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
  AreaRuleT1Model,
  AreaRuleT1PlanningModel,
  AreaRuleT2Model,
  AreaRuleT2PlanningModel,
  AreaRuleT3Model,
  AreaRuleT4PlanningModel,
  AreaRuleT5Model,
  AreaRuleT5PlanningModel,
} from '../../../../models';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @ViewChild('frame', { static: false }) frame;
  @Output()
  updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> = new EventEmitter<AreaRulePlanningModel>();
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

  show(
    rule: AreaRuleSimpleModel,
    selectedPropertyId: number,
    planning?: AreaRulePlanningModel
  ) {
    this.selectedAreaRulePlanning = planning
      ? { ...planning, propertyId: selectedPropertyId }
      : {
          ...new AreaRulePlanningModel(),
          typeSpecificFields: this.generateRulePlanningTypeSpecificFields(),
          ruleId: rule.id,
          propertyId: selectedPropertyId,
        };
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
      this.selectedAreaRulePlanning.assignedSites = this.selectedAreaRulePlanning.assignedSites.filter(
        (x) => x.siteId !== siteId
      );
    }
  }

  getAssignmentBySiteId(siteId: number) {
    const assignedSite = this.selectedAreaRulePlanning.assignedSites.find(
      (x) => x.siteId === siteId
    );
    return assignedSite ? assignedSite.checked : false;
  }

  generateRulePlanningTypeSpecificFields():
    | AreaRuleT1PlanningModel
    | AreaRuleT2PlanningModel
    | AreaRuleT4PlanningModel
    | AreaRuleT5PlanningModel {
    if (this.selectedArea.type === 1) {
      return { repeatEvery: 1, repeatType: 1 };
    }
    if (this.selectedArea.type === 2) {
      return {
        // type: AreaRuleT2TypesEnum.Closed,
        // alarm: AreaRuleT2AlarmsEnum.No,
        repeatEvery: 1,
        repeatType: 1,
        startDate: null,
      };
    }
    if (this.selectedArea.type === 3) {
      return { endDate: null, repeatEvery: 1, repeatType: 1 };
    }
    if (this.selectedArea.type === 4) {
      return { endDate: null, repeatEvery: 12, repeatType: 3 };
    }
    if (this.selectedArea.type === 5) {
      return {
        dayOfWeek: 1,
        repeatEvery: 1,
        repeatType: 2,
      };
    }
    return null;
  }
}
