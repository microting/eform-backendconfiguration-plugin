import {
  Component,
  EventEmitter,
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
  AreaRuleNameAndTypeSpecificFields,
  AreaRulePlanningModel,
  AreaRuleSimpleModel, TypeSpecificFieldsAreaRulePlanning,
} from '../../models';
import { add, set } from 'date-fns';
import * as R from 'ramda';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {AuthStateService} from 'src/app/common/store';
import {AreaRuleEntityListModalComponent} from '../../components';
import {TranslateService} from '@ngx-translate/core';
import {SiteDto} from 'src/app/common/models';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @ViewChild('entityListEditModal', { static: true }) entityListEditModal: AreaRuleEntityListModalComponent;
  @Output()
  updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> = new EventEmitter<AreaRulePlanningModel>();
  selectedArea: AreaModel = new AreaModel();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();
  selectedAreaRule: AreaRuleNameAndTypeSpecificFields = new AreaRuleNameAndTypeSpecificFields();
  days: number[] = R.range(1, 29);
  private standartDateTimeFormat = `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`;
  repeatTypeDay: {name: string, id: number}[] = R.map(x => {
    return {name: x === 1? 'Every' : x.toString(), id: x}
  }, R.range(1, 31));// 1, 2, ..., 29, 30.
  repeatTypeWeek: {name: string, id: number}[] = R.map(x => {
    return {name: x === 1? 'Every' : x.toString(), id: x}
  }, R.range(1, 51));// 1, 2, ..., 49, 50.
  repeatTypeMonth: {name: string, id: number}[] = R.map(x => {
    return {name: x === 1? 'Every' : x.toString(), id: x}
  }, R.range(1, 25));// 1, 2, ..., 23, 24.
  selectedSite: SiteDto = new SiteDto();
  type9assignedSite: number;

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

  constructor(
    private translate: TranslateService,
    dateTimeAdapter: DateTimeAdapter<any>,
    private authStateService: AuthStateService,) {

    dateTimeAdapter.setLocale(authStateService.currentUserLocale);
  }

  ngOnInit() {}

  show(
    rule: AreaRuleSimpleModel | AreaRuleNameAndTypeSpecificFields,
    selectedPropertyId: number,
    selectedArea: AreaModel,
    planning?: AreaRulePlanningModel
  ) {
    this.repeatTypeDay = R.map(x => {
      return {name: x === 1? this.translate.instant('Every') : x.toString(), id: x}
    }, R.range(1, 31));// 1, 2, ..., 29, 30.
    this.repeatTypeWeek = R.map(x => {
      return {name: x === 1? this.translate.instant('Every') : x.toString(), id: x}
    }, R.range(1, 51));// 1, 2, ..., 49, 50.
    this.repeatTypeMonth = R.map(x => {
      return {name: x === 1? this.translate.instant('Every') : x.toString(), id: x}
    }, R.range(1, 25));// 1, 2, ..., 23, 24.

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
        if (this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek !== rule.typeSpecificFields.dayOfWeek) {
          this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek = rule.typeSpecificFields.dayOfWeek;
        }
    }
    if(!this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery && !this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery){
      this.selectedAreaRulePlanning.sendNotifications = false;
      this.selectedAreaRulePlanning.complianceEnabled = false;
    }
    if (this.selectedArea.type == 9) {
      if (this.selectedAreaRulePlanning.assignedSites.length > 0) {
        this.selectedSite = this.selectedArea.availableWorkers.find(
          (x) => x.siteId === this.selectedAreaRulePlanning.assignedSites[0].siteId
        );
        this.type9assignedSite = this.selectedSite.siteId;
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
    if (this.selectedArea.type === 8) {
      if (!this.selectedAreaRulePlanning.typeSpecificFields.complianceModifiable) {
        this.selectedAreaRulePlanning.complianceEnabled = false;
      }
      if (!this.selectedAreaRulePlanning.typeSpecificFields.notificationsModifiable) {
        this.selectedAreaRulePlanning.sendNotifications = false;
      }
    }
    if (!this.selectedAreaRulePlanning.startDate) {
      this.selectedAreaRulePlanning.startDate = format(
        this.currentDate,
        this.standartDateTimeFormat
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

  addToArraySelect(e: any) {
    const assignmentObject = new AreaRuleAssignedSitesModel();
    assignmentObject.checked = true;
    assignmentObject.siteId = e.siteId;
    if (this.selectedArea.type !== 9) {
      this.selectedAreaRulePlanning.assignedSites = [
        ...this.selectedAreaRulePlanning.assignedSites,
        assignmentObject,
      ];
    } else {
      this.selectedAreaRulePlanning.assignedSites = [assignmentObject];
      this.type9assignedSite = assignmentObject.siteId;
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
      complianceEnabled: (rule.initialFields || this.selectedArea.initialFields) ?
        (rule.initialFields || this.selectedArea.initialFields).complianceEnabled :
        true,
    };
  }

  generateRulePlanningTypeSpecificFields(
    initialFields: AreaInitialFieldsModel | AreaRuleInitialFieldsModel
  ): TypeSpecificFieldsAreaRulePlanning {
    switch (this.selectedArea.type) {
      case 1:{
        return {
          repeatEvery: initialFields.repeatEvery,
          repeatType: initialFields.repeatType,
        };
      }
      case 2: {
        return {
          repeatEvery: 1,
          repeatType: 1,
          startDate: format(this.currentDate, this.standartDateTimeFormat),
        };
      }
      case 3: {
        return {
          endDate: format(
            this.currentDatePlusTwoWeeks,
            this.standartDateTimeFormat
          ),
          repeatEvery: 1,
          repeatType: 1,
        };
      }
      case 4: {
        return {
          startDate: format(this.currentDate, this.standartDateTimeFormat),
          repeatEvery: 12,
          repeatType: 3,
        };
      }
      case 5: {
        return {
          dayOfWeek: this.selectedAreaRule.typeSpecificFields.dayOfWeek,
          repeatEvery: this.selectedAreaRule.typeSpecificFields.repeatEvery,
          repeatType: 2,
        };
      }
      case 6: {
        return {
          repeatEvery: 12,
          repeatType: 3,
          hoursAndEnergyEnabled: true,
        };
      }
      case 7: {
        return {
          startDate: format(this.currentDate, this.standartDateTimeFormat),
        };
      }
      case 8: {
        this.selectedAreaRulePlanning.sendNotifications = this.selectedAreaRule.typeSpecificFields.notifications;
        this.selectedAreaRulePlanning.complianceEnabled = this.selectedAreaRule.typeSpecificFields.complianceEnabled;
        return {
          startDate: format(this.currentDate, this.standartDateTimeFormat),
          dayOfWeek: this.selectedAreaRule.typeSpecificFields.dayOfWeek,
          repeatEvery: this.selectedAreaRule.typeSpecificFields.repeatEvery,
          repeatType: this.selectedAreaRule.typeSpecificFields.repeatType,
          complianceEnabled: this.selectedAreaRule.typeSpecificFields.complianceEnabled,
          complianceModifiable: this.selectedAreaRule.typeSpecificFields.complianceModifiable,
          notifications: this.selectedAreaRule.typeSpecificFields.notifications,
          notificationsModifiable: this.selectedAreaRule.typeSpecificFields.notificationsModifiable,
        };
      }
      case 9: {
        return {
          startDate: format(this.currentDate, this.standartDateTimeFormat),
        };
      }
      case 10: {
        return {
          startDate: format(this.currentDate, this.standartDateTimeFormat),
          repeatEvery: 0,
          repeatType: 0,
        };
      }
      default: {
        return null;
      }
    }
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
      this.standartDateTimeFormat
    );
  }

  /*updateEndDate(e: any) {
    let date = new Date(e._d);
    date = set(date, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
    this.selectedAreaRulePlanning.typeSpecificFields.endDate = format(
      date,
      this.standartDateTimeFormat
    );
  }*/

  isDisabledSaveBtn() {
    if (this.selectedArea.type !== 9) {
      return !this.selectedAreaRulePlanning.assignedSites.some((x) => x.checked);
    }
  }

  repeatTypeMass() {
    switch (this.selectedAreaRulePlanning.typeSpecificFields.repeatType) {
      case 1: { // day
        return this.repeatTypeDay;
      }
      case 2: { // week
        return this.repeatTypeWeek;
      }
      case 3: { // month
        return this.repeatTypeMonth;
      }
    }
  }

  onChangeRepeatEvery(repeatEvery: number) {
    if(this.selectedAreaRulePlanning.typeSpecificFields.repeatType === 1 && repeatEvery === 1){
      this.selectedAreaRulePlanning.sendNotifications = false;
      this.selectedAreaRulePlanning.complianceEnabled = false;
    }
    this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery = repeatEvery;
  }

  onChangeRepeatType(repeatType: number) {
    this.selectedAreaRulePlanning.typeSpecificFields.repeatType = repeatType;
    this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery = null
    this.selectedAreaRulePlanning.sendNotifications = false;
    this.selectedAreaRulePlanning.complianceEnabled = false;
  }

  getShowCompliance(): boolean {
    if(this.selectedArea.type === 3) {
      return false;
    }
    if(this.selectedArea.type === 4 ||
       this.selectedAreaRulePlanning.typeSpecificFields &&
       this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery) {
      return true;
    }
    if (this.selectedArea.type === 8) {
      return this.selectedAreaRulePlanning.typeSpecificFields.complianceModifiable ;
    }
  }

}
