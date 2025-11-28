import {
  Component,
  EventEmitter,
  OnInit,
  inject
} from '@angular/core';
import {format} from 'date-fns';
import {
  AreaInitialFieldsModel,
  AreaModel,
  AreaRuleAssignedSitesModel,
  AreaRuleInitialFieldsModel,
  AreaRuleNameAndTypeSpecificFields,
  AreaRulePlanningModel,
  AreaRuleSimpleModel,
  TypeSpecificFieldsAreaRulePlanning,
} from '../../models';
import {add, set} from 'date-fns';
import * as R from 'ramda';
import {TranslateService} from '@ngx-translate/core';
import {SiteDto} from 'src/app/common/models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn, MtxGridRowSelectionFormatter} from '@ng-matero/extensions/grid';
import {MatDatepickerInputEvent} from '@angular/material/datepicker';
import {PARSING_DATE_FORMAT} from 'src/app/common/const';
import {generateWeeksList} from '../../helpers';

@Component({
    selector: 'app-area-rule-plan-modal',
    templateUrl: './area-rule-plan-modal.component.html',
    styleUrls: ['./area-rule-plan-modal.component.scss'],
    standalone: false
})
export class AreaRulePlanModalComponent implements OnInit {
  private translate = inject(TranslateService);
  public dialogRef = inject(MatDialogRef<AreaRulePlanModalComponent>);
  private model = inject<{
      areaRule: AreaRuleSimpleModel | AreaRuleNameAndTypeSpecificFields,
      propertyId: number,
      area: AreaModel,
      areaRulePlan: AreaRulePlanningModel,
    }>(MAT_DIALOG_DATA);

  updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> = new EventEmitter<AreaRulePlanningModel>();
  selectedArea: AreaModel = new AreaModel();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();
  selectedAreaRule: AreaRuleNameAndTypeSpecificFields = new AreaRuleNameAndTypeSpecificFields();
  daysOfMonth: number[] = R.range(1, 29);
  /**
   * 1, 2, ..., 29, 30.
   */
  repeatTypeDay: { name: string, id: number }[] = R.map(x => {
    return {name: x === 1 ? 'Every' : x.toString(), id: x};
  }, R.range(1, 31));
  /**
   * 1, 2, ..., 49, 50.
   */
  repeatTypeWeek: { name: string, id: number }[] = R.map(x => {
    return {name: x === 1 ? 'Every' : x.toString(), id: x};
  }, R.range(1, 51));
  /**
   * 1, 2, ..., 23, 24.
   */
  repeatTypeMonth: { name: string, id: number }[] = R.map(x => {
    return {name: x === 1 ? 'Every' : x.toString(), id: x};
    // }, R.range(1, 25));
  }, [1, 2, 3, 6, 12, 24]);
  dayOfWeekArr: { id: number, name: string }[];
  repeatEveryArr: { id: number, name: string }[];
  repeatTypeArr: { id: number, name: string }[];
  selectedSite: SiteDto = new SiteDto();
  type9assignedSite: number;

  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translate.stream('ID'),
      field: 'siteId',
    },
    {
      header: this.translate.stream('Worker name'),
      field: 'siteName',
    },
  ];
  rowSelectionFormatter: MtxGridRowSelectionFormatter = {
    disabled: () => !this.selectedAreaRulePlanning.status,
    hideCheckbox: () => false,
  };

  get currentDate() {
    return set(new Date(), {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
  }

  get currentDatePlusTwoWeeks() {
    return add(this.currentDate, {weeks: 2});
  }

  get showAreaRuleNotificationsToggle(): boolean {
    return this.selectedAreaRulePlanning.typeSpecificFields &&
      this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery &&
      this.selectedArea.type !== 3 &&
      this.selectedArea.type !== 10 &&
      !!this.selectedAreaRule.typeSpecificFields &&
      this.selectedAreaRule.typeSpecificFields.notificationsModifiable;
  }

  get selectedWorkers() {
    const assignedSites = this.selectedAreaRulePlanning.assignedSites.filter(x => x.checked).map(x => x.siteId);
    return this.selectedArea.availableWorkers.filter(x => assignedSites.some(y => y === x.siteId));
  }

  

  ngOnInit() {
    this.setData(this.model.areaRule, this.model.propertyId, this.model.area, this.model.areaRulePlan);

    this.repeatTypeDay = R.map(x => {
      return {name: x === 1 ? this.translate.instant('Every') : x.toString(), id: x};
  }, R.range(1, 31)); // 1, 2, ..., 29, 30.
    this.repeatTypeWeek = R.map(x => {
      return {name: x === 1 ? this.translate.instant('Every') : x.toString(), id: x};
    }, R.range(1, 51)); // 1, 2, ..., 49, 50.
    this.repeatTypeMonth = [
      {id: 1, name: this.translate.instant('Every month')},
      {id: 2, name: this.translate.instant('2nd months')},
      {id: 3, name: this.translate.instant('3rd months')},
      {id: 6, name: this.translate.instant('6th months')},
      {id: 12, name: this.translate.instant('12 (1 year)')},
      {id: 24, name: this.translate.instant('24 (2 years)')},
      {id: 36, name: this.translate.instant('36 (3 years)')},
      {id: 48, name: this.translate.instant('48 (4 years)')},
      {id: 60, name: this.translate.instant('60 (5 years)')},
      {id: 72, name: this.translate.instant('72 (6 years)')},
      {id: 84, name: this.translate.instant('84 (7 years)')},
      {id: 96, name: this.translate.instant('96 (8 years)')},
      {id: 108, name: this.translate.instant('108 (9 years)')},
      {id: 120, name: this.translate.instant('120 (10 years)')},
    ]; // 1, 2, ..., 23, 24.
    // }, R.range(1, 25)); // 1, 2, ..., 23, 24.
    this.dayOfWeekArr = [
      {id: 1, name: this.translate.instant('Monday')},
      {id: 2, name: this.translate.instant('Tuesday')},
      {id: 3, name: this.translate.instant('Wednesday')},
      {id: 4, name: this.translate.instant('Thursday')},
      {id: 5, name: this.translate.instant('Friday')},
      {id: 6, name: this.translate.instant('Saturday')},
      {id: 0, name: this.translate.instant('Sunday')}
    ];
    this.repeatEveryArr = generateWeeksList(this.translate, 52);
    this.repeatTypeArr = [
      {id: 1, name: this.translate.instant('Day')},
      {id: 2, name: this.translate.instant('Week')},
      {id: 3, name: this.translate.instant('Month')},
    ];
  }

  setData(
    rule: AreaRuleSimpleModel | AreaRuleNameAndTypeSpecificFields,
    selectedPropertyId: number,
    selectedArea: AreaModel,
    planning?: AreaRulePlanningModel
  ) {
    this.selectedArea = {...selectedArea};
    this.selectedAreaRule = {...rule};
    if (planning) {
      this.selectedAreaRulePlanning = {...planning, propertyId: selectedPropertyId};
      this.selectedAreaRulePlanning.status = planning.serverStatus;
    }
    if (!planning) {
      this.selectedAreaRulePlanning = this.generateInitialPlanningObject({
        eformName: '',
        id: 0,
        isDefault: false,
        planningStatus: false,
        secondaryeFormId: 0,
        repeatType: 0,
        repeatEvery: 0,
        ...rule
      }, selectedPropertyId);
    }

    // this.selectedAreaRulePlanning.complianceEnabled = planning.complianceEnabled;

    if (this.selectedArea.type === 5) {
      if (this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek !== rule.typeSpecificFields.dayOfWeek) {
        this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek = rule.typeSpecificFields.dayOfWeek;
      }
    }
    if (this.selectedArea.type === 9) {
      if (this.selectedAreaRulePlanning.assignedSites.length > 0) {
        this.selectedSite = this.selectedArea.availableWorkers.find(
          (x) => x.siteId === this.selectedAreaRulePlanning.assignedSites[0].siteId
        );
        this.type9assignedSite = this.selectedSite.siteId;
      }
    }
    if (!this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery && !this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery) {
      this.selectedAreaRulePlanning.sendNotifications = false;
      this.selectedAreaRulePlanning.complianceEnabled = false;
    }
  }

  hide() {
    this.selectedArea = new AreaModel();
    this.selectedAreaRulePlanning = new AreaRulePlanningModel();
    this.selectedAreaRule = new AreaRuleNameAndTypeSpecificFields();
    this.dialogRef.close();
  }

  onUpdateAreaRulePlan() {
    if (this.selectedArea.type === 8) {
      // if (!this.selectedAreaRulePlanning.typeSpecificFields.complianceModifiable) {
      //   this.selectedAreaRulePlanning.complianceEnabled = false;
      // }
      // if (!this.selectedAreaRulePlanning.typeSpecificFields.notificationsModifiable) {
      //   this.selectedAreaRulePlanning.sendNotifications = false;
      // }
    }
    if (!this.selectedAreaRulePlanning.startDate) {
      this.selectedAreaRulePlanning.startDate = this.currentDate;
    }
    this.updateAreaRulePlan.emit({
      ...this.selectedAreaRulePlanning,
      startDate: format(this.selectedAreaRulePlanning.startDate as Date, PARSING_DATE_FORMAT)
    });
  }

  addToArray(e: any, siteId: number) {
    const assignmentObject = new AreaRuleAssignedSitesModel();
    if (e.checked) {
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
    assignmentObject.status = 0;
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

  getAssignmentBySiteId(siteId: number): string {
    const assignedSite = this.selectedAreaRulePlanning.assignedSites.find(
      (x) => x.siteId === siteId
    );
    return !assignedSite ? 'false' : (assignedSite.checked ? 'true' : 'false');
  }

  getLatestCaseStatus(siteId: number) {
    const assignedSite = this.selectedAreaRulePlanning.assignedSites.find(
      (x) => x.siteId === siteId
    );
    if (assignedSite) {
      if (assignedSite.status !== undefined) {
        return assignedSite.status;
      } else {
        return 0;
      }
    } else {
      return 0;
    }
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
      typeSpecificFields: {...initialFields},
      ruleId: rule.id,
      propertyId,
      status: true,
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
      case 1: {
        return {
          repeatEvery: initialFields.repeatEvery,
          repeatType: initialFields.repeatType,
        };
      }
      case 2: {
        return {
          repeatEvery: 1,
          repeatType: 1,
          startDate: this.currentDate,
        };
      }
      case 3: {
        return {
          endDate: format(
            this.currentDatePlusTwoWeeks,
            PARSING_DATE_FORMAT
          ),
          repeatEvery: 1,
          repeatType: 1,
        };
      }
      case 4: {
        return {
          startDate: this.currentDate,
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
          startDate: this.currentDate,
        };
      }
      case 8: {
        this.selectedAreaRulePlanning.sendNotifications = this.selectedAreaRule.typeSpecificFields.notifications;
        this.selectedAreaRulePlanning.complianceEnabled = this.selectedAreaRule.typeSpecificFields.complianceEnabled;
        return {
          startDate: this.currentDate,
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
          startDate: this.currentDate,
        };
      }
      case 10: {
        return {
          startDate: this.currentDate,
          repeatEvery: 0,
          repeatType: 0,
        };
      }
      default: {
        return null;
      }
    }
  }

  updateStartDate(e: MatDatepickerInputEvent<any, any>) {
    let date = new Date(e.value);
    date = set(date, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
      date: date.getDate(),
      year: date.getFullYear(),
      month: date.getMonth(),
    });
    this.selectedAreaRulePlanning.startDate = date;
  }

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
      default: {
        return [];
      }
    }
  }

  onChangeRepeatEvery(repeatEvery: number) {
    if (this.selectedAreaRulePlanning.typeSpecificFields.repeatType === 1 && repeatEvery === 0) {
      this.selectedAreaRulePlanning.sendNotifications = false;
      this.selectedAreaRulePlanning.typeSpecificFields.complianceModifiable = false;
      this.selectedAreaRulePlanning.typeSpecificFields.notificationsModifiable = false;
      this.selectedAreaRulePlanning.complianceEnabled = false;
    }
    if (this.selectedAreaRulePlanning.typeSpecificFields.repeatType === 1 && repeatEvery === 1) {
      this.selectedAreaRulePlanning.sendNotifications = false;
      this.selectedAreaRulePlanning.complianceEnabled = false;
    }
    if (this.selectedAreaRulePlanning.typeSpecificFields.repeatType === 3 && repeatEvery === 1) {
      this.selectedAreaRulePlanning.typeSpecificFields.dayOfMonth = 1;
    }
    if (this.selectedAreaRulePlanning.typeSpecificFields.repeatType === 2) {
      if (this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek === 0) {
        this.selectedAreaRulePlanning.typeSpecificFields.dayOfWeek = 1;
      }
    }
    this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery = repeatEvery;
  }

  onChangeRepeatType(repeatType: number) {
    this.selectedAreaRulePlanning.typeSpecificFields.repeatType = repeatType;
    this.selectedAreaRulePlanning.typeSpecificFields.repeatEvery = 1;
  }
}
