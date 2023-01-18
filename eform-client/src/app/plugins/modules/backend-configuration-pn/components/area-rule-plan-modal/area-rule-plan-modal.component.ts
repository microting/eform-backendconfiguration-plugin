import {
  Component,
  EventEmitter,
  Inject,
  OnInit,
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
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {AuthStateService} from 'src/app/common/store';
import {TranslateService} from '@ngx-translate/core';
import {SiteDto} from 'src/app/common/models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn, MtxGridRowSelectionFormatter} from '@ng-matero/extensions/grid';

@Component({
  selector: 'app-area-rule-plan-modal',
  templateUrl: './area-rule-plan-modal.component.html',
  styleUrls: ['./area-rule-plan-modal.component.scss'],
})
export class AreaRulePlanModalComponent implements OnInit {
  updateAreaRulePlan: EventEmitter<AreaRulePlanningModel> = new EventEmitter<AreaRulePlanningModel>();
  selectedArea: AreaModel = new AreaModel();
  selectedAreaRulePlanning: AreaRulePlanningModel = new AreaRulePlanningModel();
  selectedAreaRule: AreaRuleNameAndTypeSpecificFields = new AreaRuleNameAndTypeSpecificFields();
  daysOfMonth: number[] = R.range(1, 29);
  private standartDateTimeFormat = `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`;
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
  }, R.range(1, 25));
  dayOfWeekArr: { id: number, name: string }[];
  repeatEveryArr: { id: number, name: string }[];
  repeatTypeArr: { id: number, name: string }[];
  selectedSite: SiteDto = new SiteDto();
  type9assignedSite: number;

  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'siteId',
    },
    {
      header: this.translateService.stream('Worker name'),
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

  constructor(
    private translate: TranslateService,
    dateTimeAdapter: DateTimeAdapter<any>,
    private authStateService: AuthStateService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<AreaRulePlanModalComponent>,
    @Inject(MAT_DIALOG_DATA) model: {
      areaRule: AreaRuleSimpleModel | AreaRuleNameAndTypeSpecificFields,
      propertyId: number,
      area: AreaModel,
      areaRulePlan: AreaRulePlanningModel,
    },
  ) {
    this.setData(model.areaRule, model.propertyId, model.area, model.areaRulePlan);
    dateTimeAdapter.setLocale(authStateService.currentUserLocale);
  }

  ngOnInit() {
    this.repeatTypeDay = R.map(x => {
      return {name: x === 1 ? this.translate.instant('Every') : x.toString(), id: x};
    }, R.range(1, 31)); // 1, 2, ..., 29, 30.
    this.repeatTypeWeek = R.map(x => {
      return {name: x === 1 ? this.translate.instant('Every') : x.toString(), id: x};
    }, R.range(1, 51)); // 1, 2, ..., 49, 50.
    this.repeatTypeMonth = R.map(x => {
      return {name: x === 1 ? this.translate.instant('Every') : x.toString(), id: x};
    }, R.range(1, 25)); // 1, 2, ..., 23, 24.
    this.dayOfWeekArr = [
      {id: 1, name: this.translate.instant('Monday')},
      {id: 2, name: this.translate.instant('Tuesday')},
      {id: 3, name: this.translate.instant('Wednesday')},
      {id: 4, name: this.translate.instant('Thursday')},
      {id: 5, name: this.translate.instant('Friday')},
      {id: 6, name: this.translate.instant('Saturday')},
      {id: 0, name: this.translate.instant('Sunday')}
    ];
    this.repeatEveryArr = [
      {id: 1, name: this.translate.instant('Weekly')},
      {id: 2, name: this.translate.instant('2nd week')},
      {id: 3, name: this.translate.instant('3rd week')},
      {id: 4, name: this.translate.instant('4th week')},
      {id: 5, name: this.translate.instant('5th week')},
      {id: 6, name: this.translate.instant('6th week')},
      {id: 7, name: this.translate.instant('7th week')},
      {id: 8, name: this.translate.instant('8th week')},
      {id: 9, name: this.translate.instant('9th week')},
      {id: 10, name: this.translate.instant('10th week')},
      {id: 11, name: this.translate.instant('11st week')},
      {id: 12, name: this.translate.instant('12nd week')},
      {id: 13, name: this.translate.instant('13rd week')},
      {id: 14, name: this.translate.instant('14th week')},
      {id: 15, name: this.translate.instant('15th week')},
      {id: 16, name: this.translate.instant('16th week')},
      {id: 17, name: this.translate.instant('17th week')},
      {id: 18, name: this.translate.instant('18th week')},
      {id: 19, name: this.translate.instant('19th week')},
      {id: 20, name: this.translate.instant('20th week')},
      {id: 21, name: this.translate.instant('21st week')},
      {id: 22, name: this.translate.instant('22nd week')},
      {id: 23, name: this.translate.instant('23rd week')},
      {id: 24, name: this.translate.instant('24th week')},
      {id: 25, name: this.translate.instant('25th week')},
      {id: 26, name: this.translate.instant('26th week')},
      {id: 27, name: this.translate.instant('27th week')},
      {id: 28, name: this.translate.instant('28th week')},
      {id: 29, name: this.translate.instant('29th week')},
      {id: 30, name: this.translate.instant('30th week')},
      {id: 31, name: this.translate.instant('31st week')},
      {id: 32, name: this.translate.instant('32nd week')},
      {id: 33, name: this.translate.instant('33rd week')},
      {id: 34, name: this.translate.instant('34th week')},
      {id: 35, name: this.translate.instant('35th week')},
      {id: 36, name: this.translate.instant('36th week')},
      {id: 37, name: this.translate.instant('37th week')},
      {id: 38, name: this.translate.instant('38th week')},
      {id: 39, name: this.translate.instant('39th week')},
      {id: 40, name: this.translate.instant('40th week')},
      {id: 41, name: this.translate.instant('41st week')},
      {id: 42, name: this.translate.instant('42nd week')},
      {id: 43, name: this.translate.instant('43rd week')},
      {id: 44, name: this.translate.instant('44th week')},
      {id: 45, name: this.translate.instant('45th week')},
      {id: 46, name: this.translate.instant('46th week')},
      {id: 47, name: this.translate.instant('47th week')},
      {id: 48, name: this.translate.instant('48th week')},
      {id: 49, name: this.translate.instant('49th week')},
      {id: 50, name: this.translate.instant('50th week')},
      {id: 51, name: this.translate.instant('51st week')},
      {id: 52, name: this.translate.instant('52nd week')},
    ];
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
    }
    if (!planning) {
      this.selectedAreaRulePlanning = this.generateInitialPlanningObject({
        eformName: '',
        id: 0,
        isDefault: false,
        planningStatus: false,
        secondaryeFormId: 0,
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
      this.selectedAreaRulePlanning.startDate = format(
        this.currentDate,
        this.standartDateTimeFormat
      );
    }
    this.updateAreaRulePlan.emit({...this.selectedAreaRulePlanning});
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
    let date = new Date(e/*._d*/);
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
