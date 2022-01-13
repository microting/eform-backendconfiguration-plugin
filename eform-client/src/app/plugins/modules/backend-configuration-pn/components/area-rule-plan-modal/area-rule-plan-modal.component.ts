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
  AreaRuleInitialFieldsModel, AreaRuleNameAndTypeSpecificFields,
  AreaRulePlanningModel,
  AreaRuleSimpleModel,
  AreaRuleT1PlanningModel,
  AreaRuleT2PlanningModel,
  AreaRuleT4PlanningModel,
  AreaRuleT5PlanningModel,
} from '../../models';
import { add, set } from 'date-fns';
import * as R from 'ramda';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output()
  updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> = new EventEmitter<AreaRulePlanningModel>();
  selectedArea: AreaModel = new AreaModel();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();
  selectedAreaRule: AreaRuleNameAndTypeSpecificFields = new AreaRuleNameAndTypeSpecificFields();
  days: number[] = R.range(1, 29);

  get currentDate() {
    return set(new Date(), {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
  }

  get currentDatePlusTwoWeeks() {
    return add(this.currentDate, { weeks: 2 });
  }

  constructor() {}

  ngOnInit() {}

  show(
    rule: AreaRuleSimpleModel | AreaRuleNameAndTypeSpecificFields,
    selectedPropertyId: number,
    selectedArea: AreaModel,
    planning?: AreaRulePlanningModel
  ) {
    this.selectedArea = {...selectedArea};
    this.selectedAreaRule = { ...rule };
    if (planning) {
      this.selectedAreaRulePlanning = {...planning, propertyId: selectedPropertyId};
    }
    if (!planning){
      this.selectedAreaRulePlanning = this.generateInitialPlanningObject({
        eformName: '',
        id: 0,
        isDefault: false,
        planningStatus: false,
        ...rule}, selectedPropertyId);
    }
    if (this.selectedArea.type === 5) {
      this.selectedAreaRulePlanning.typeSpecificFields = <AreaRuleT5PlanningModel> this.selectedAreaRulePlanning.typeSpecificFields;
        if (this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek !== rule.typeSpecificFields.dayOfWeek) {
          this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek = rule.typeSpecificFields.dayOfWeek;
        }
    }
    this.frame.show();
  }

  hide() {
    this.selectedArea = new AreaModel();
    this.selectedAreaRulePlanning = new AreaRulePlanningModel();
    this.selectedAreaRule = new AreaRuleNameAndTypeSpecificFields();
    this.frame.hide();
  }

  onUpdateAreaRulePlan() {
    if (!this.selectedAreaRulePlanning.startDate) {
      this.selectedAreaRulePlanning.startDate = format(
        this.currentDate,
        `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`
      );
    }
    this.updateAreaRulePlan.emit({ ...this.selectedAreaRulePlanning });
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
      // @ts-ignore
      startDate: initialFields.startDate,
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
        startDate: format(this.currentDate, `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`),
      };
    }
    if (this.selectedArea.type === 3) {
      return {
        endDate: format(
          this.currentDatePlusTwoWeeks,
          `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`
        ),
        repeatEvery: 1,
        repeatType: 1,
      };
    }
    if (this.selectedArea.type === 4) {
      return {
        startDate: format(this.currentDate, `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`),
        repeatEvery: 12,
        repeatType: 3,
      };
    }
    if (this.selectedArea.type === 5) {
      return {
        dayOfWeek: this.selectedAreaRule.typeSpecificFields.dayOfWeek,
        repeatEvery: this.selectedAreaRule.typeSpecificFields.repeatEvery,
        repeatType: 2,
      };
    }
    if (this.selectedArea.type === 6) {
      return {
        repeatEvery: 12,
        repeatType: 3,
        // @ts-ignore
        hoursAndEnergyEnabled: true,
      };
    }
    if (this.selectedArea.type === 7) {
      // @ts-ignore
      return {
        startDate: format(this.currentDate, `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`),
      };
    }
    return null;
  }

  updateStartDate(e: any) {
    let date = new Date(e._d);
    date = set(date, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
    this.selectedAreaRulePlanning.startDate = format(
      date,
      `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`
    );
  }

  updateEndDate(e: any) {
    if (
      this.selectedAreaRulePlanning.typeSpecificFields instanceof
      AreaRuleT4PlanningModel
    ) {
      let date = new Date(e._d);
      date = set(date, {
        hours: 0,
        minutes: 0,
        seconds: 0,
        milliseconds: 0,
      });
      this.selectedAreaRulePlanning.typeSpecificFields.endDate = format(
        date,
        `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`
      );
    }
  }

  isDisabledSaveBtn() {
    return !this.selectedAreaRulePlanning.assignedSites.some((x) => x.checked);
  }
}
