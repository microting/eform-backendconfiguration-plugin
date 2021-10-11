import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { format } from 'date-fns';
import {
  AreaInitialFieldsModel,
  AreaModel,
  AreaRuleAssignedSitesModel,
  AreaRuleInitialFieldsModel,
  AreaRulePlanningModel,
  AreaRuleSimpleModel,
  AreaRuleT1PlanningModel,
  AreaRuleT2PlanningModel,
  AreaRuleT4PlanningModel,
  AreaRuleT5PlanningModel,
} from '../../../../models';

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
      : this.generateInitialPlanningObject(rule, selectedPropertyId);
    this.selectedAreaRule = { ...rule };
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

  generateInitialPlanningObject(
    rule: AreaRuleSimpleModel,
    propertyId: number
  ): AreaRulePlanningModel {
    const initialFields = rule.initialFields
      ? this.generateRulePlanningTypeSpecificFields(rule.initialFields)
      : this.generateRulePlanningTypeSpecificFields(
          this.selectedArea.initialFields
        );

    return {
      ...new AreaRulePlanningModel(),
      typeSpecificFields: { ...initialFields },
      ruleId: rule.id,
      propertyId,
      status: true,
      sendNotifications: rule.initialFields
        ? rule.initialFields.sendNotifications
        : this.selectedArea.initialFields.sendNotifications,
    };
  }

  generateRulePlanningTypeSpecificFields(
    initialFields: AreaInitialFieldsModel | AreaRuleInitialFieldsModel
  ):
    | AreaRuleT1PlanningModel
    | AreaRuleT2PlanningModel
    | AreaRuleT4PlanningModel
    | AreaRuleT5PlanningModel {
    if (this.selectedArea.type === 1) {
      return {
        repeatEvery: initialFields.repeatEvery,
        repeatType: initialFields.repeatType,
      };
    }
    if (this.selectedArea.type === 2) {
      return {
        repeatEvery: 1,
        repeatType: 1,
        startDate: format(new Date(), 'yyyy-MM-dd'),
      };
    }
    if (this.selectedArea.type === 3) {
      return {
        endDate: format(new Date(Date.now() + 12096e5), 'yyyy-MM-dd'),
        repeatEvery: 1,
        repeatType: 1,
      };
    }
    if (this.selectedArea.type === 4) {
      return {
        endDate: format(new Date(Date.now() + 12096e5), 'yyyy-MM-dd'),
        repeatEvery: 12,
        repeatType: 3,
      };
    }
    if (this.selectedArea.type === 5) {
      return {
        dayOfWeek: 1,
        repeatEvery: 1,
        repeatType: 2,
      };
    }
    if (this.selectedArea.type === 6) {
      return {
        repeatEvery: 12,
        repeatType: 3,
      };
    }
    return null;
  }
}
