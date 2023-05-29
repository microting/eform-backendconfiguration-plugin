import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Inject,
  OnInit,
  TemplateRef,
  ViewChild,
} from '@angular/core';
import {debounceTime, switchMap} from 'rxjs/operators';
import {TemplateListModel, TemplateRequestModel} from 'src/app/common/models';
import {EFormService} from 'src/app/common/services';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from '../../../../enums';
import {
  AreaModel,
  AreaRulesCreateModel, AreaRuleSimpleModel,
  AreaRuleTypeSpecificFields,
  PoolHourModel,
  PoolHoursModel,
} from '../../../../models';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {TranslateService} from '@ngx-translate/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn, MtxGridCellTemplate} from '@ng-matero/extensions/grid';

@Component({
  selector: 'app-area-rule-create-modal',
  templateUrl: './area-rule-create-modal.component.html',
  styleUrls: ['./area-rule-create-modal.component.scss'],
})
export class AreaRuleCreateModalComponent implements OnInit {
  @ViewChild('checkboxTpl', { static: true }) checkboxTpl!: TemplateRef<any>;
  @ViewChild('weekdaysTpl', { static: true }) weekdaysTpl!: TemplateRef<any>;
  selectedArea: AreaModel = new AreaModel();
  areaRules: AreaRuleSimpleModel[] = [];
  createAreaRule: EventEmitter<AreaRulesCreateModel> = new EventEmitter<AreaRulesCreateModel>();
  deleteAreaRule: EventEmitter<number[]> = new EventEmitter<number[]>();
  newAreaRules: AreaRulesCreateModel = new AreaRulesCreateModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
  newAreaRulesString: string;
  newAreaRulesDayOfWeek: number | null;
  newAreaRulesRepeatEvery = 1;
  typeahead = new EventEmitter<string>();
  templatesModel: TemplateListModel = new TemplateListModel();
  areaRulesForType7: { folderName: string; areaRuleNames: string[] }[] = [];
  areaRulesForType8: { folderName: string; areaRuleNames: string[] }[] = [];
  newAreaRulesForType7: string[] = [];
  newAreaRulesForType8: string[] = [];
  days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  hours = ['00', '01', '02', '03' ,'04' ,'05', '06', '07', '08', '09', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20', '21', '22', '23'];
  daysOfWeek = [
    { id: 1, name: this.translateService.instant('Monday') },
    { id: 2, name: this.translateService.instant('Tuesday') },
    { id: 3, name: this.translateService.instant('Wednesday') },
    { id: 4, name: this.translateService.instant('Thursday') },
    { id: 5, name: this.translateService.instant('Friday') },
    { id: 6, name: this.translateService.instant('Saturday') },
    { id: 0, name: this.translateService.instant('Sunday') }
  ];
  repeatEveryArr = [
    { id: 1, name: this.translateService.instant('Weekly') },
    { id: 2, name: this.translateService.instant('2nd week') },
    { id: 3, name: this.translateService.instant('3rd week') },
    { id: 4, name: this.translateService.instant('4th week') },
    { id: 5, name: this.translateService.instant('5th week') },
    { id: 6, name: this.translateService.instant('6th week') },
    { id: 7, name: this.translateService.instant('7th week') },
    { id: 8, name: this.translateService.instant('8th week') },
    { id: 9, name: this.translateService.instant('9th week') },
    { id: 10, name: this.translateService.instant('10th week') },
    { id: 11, name: this.translateService.instant('11st week') },
    { id: 12, name: this.translateService.instant('12nd week') },
    { id: 13, name: this.translateService.instant('13rd week') },
    { id: 14, name: this.translateService.instant('14th week') },
    { id: 15, name: this.translateService.instant('15th week') },
    { id: 16, name: this.translateService.instant('16th week') },
    { id: 17, name: this.translateService.instant('17th week') },
    { id: 18, name: this.translateService.instant('18th week') },
    { id: 19, name: this.translateService.instant('19th week') },
    { id: 20, name: this.translateService.instant('20th week') },
    { id: 21, name: this.translateService.instant('21st week') },
    { id: 22, name: this.translateService.instant('22nd week') },
    { id: 23, name: this.translateService.instant('23rd week') },
    { id: 24, name: this.translateService.instant('24th week') },
    { id: 25, name: this.translateService.instant('25th week') },
    { id: 26, name: this.translateService.instant('26th week') },
    { id: 27, name: this.translateService.instant('27th week') },
    { id: 28, name: this.translateService.instant('28th week') },
    { id: 29, name: this.translateService.instant('29th week') },
    { id: 30, name: this.translateService.instant('30th week') },
    { id: 31, name: this.translateService.instant('31st week') },
    { id: 32, name: this.translateService.instant('32nd week') },
    { id: 33, name: this.translateService.instant('33rd week') },
    { id: 34, name: this.translateService.instant('34th week') },
    { id: 35, name: this.translateService.instant('35th week') },
    { id: 36, name: this.translateService.instant('36th week') },
    { id: 37, name: this.translateService.instant('37th week') },
    { id: 38, name: this.translateService.instant('38th week') },
    { id: 39, name: this.translateService.instant('39th week') },
    { id: 40, name: this.translateService.instant('40th week') },
    { id: 41, name: this.translateService.instant('41st week') },
    { id: 42, name: this.translateService.instant('42nd week') },
    { id: 43, name: this.translateService.instant('43rd week') },
    { id: 44, name: this.translateService.instant('44th week') },
    { id: 45, name: this.translateService.instant('45th week') },
    { id: 46, name: this.translateService.instant('46th week') },
    { id: 47, name: this.translateService.instant('47th week') },
    { id: 48, name: this.translateService.instant('48th week') },
    { id: 49, name: this.translateService.instant('49th week') },
    { id: 50, name: this.translateService.instant('50th week') },
    { id: 51, name: this.translateService.instant('51st week') },
    { id: 52, name: this.translateService.instant('52nd week') },
  ];

  tableHeaders: MtxGridColumn[] = [
    {
      field: 'weekdays',
      header: this.translateService.stream('Weekdays'),
    },
    ...this.hours
      .map((x, index): MtxGridColumn => {
        return {
          field: `${index}.isActive`,
          header: `kl ${x}:00`,
          // @ts-ignore
          index: index,
        }
      }),
  ];
  templates: MtxGridCellTemplate = {};

  dataForTable: Array<Array<Array<PoolHourModel>>> = [];

  constructor(
    private eFormService: EFormService,
    private cd: ChangeDetectorRef,
    private backendConfigurationPnAreasService: BackendConfigurationPnAreasService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<AreaRuleCreateModalComponent>,
    @Inject(MAT_DIALOG_DATA) model: {selectedArea: AreaModel, areaRules: AreaRuleSimpleModel[]}
  ) {

    this.selectedArea = model.selectedArea;
    this.areaRules = model.areaRules;

    this.typeahead
      .pipe(
        debounceTime(200),
        switchMap((term) => {
          this.templateRequestModel.nameFilter = term;
          return this.eFormService.getAll(this.templateRequestModel);
        })
      )
      .subscribe((items) => {
        this.templatesModel = items.model;
        this.cd.markForCheck();
      });

    if (this.selectedArea.type === 7) {
      this.backendConfigurationPnAreasService
        .getAreaRulesForType7()
        .subscribe((data) => {
          if (data.success) {
            this.areaRulesForType7 = data.model;
          }
        });
      this.areaRules.forEach(x => this.newAreaRulesForType7 = [...this.newAreaRulesForType7, x.translatedName]);
    } else if (this.selectedArea.type === 8) {
      this.backendConfigurationPnAreasService
        .getAreaRulesForType8()
        .subscribe((data) => {
          if (data.success) {
            this.areaRulesForType8 = data.model;
          }
        });
      this.areaRules.forEach(x => this.newAreaRulesForType8 = [...this.newAreaRulesForType8, x.translatedName]);
    }
  }

  ngOnInit() {
    this.hours.forEach((x, index) => {
      this.templates = {...this.templates, [`${index}.isActive`]: this.checkboxTpl};
    });
    this.templates = {...this.templates, weekdays: this.weekdaysTpl};
  }

  hide() {
    this.newAreaRules = new AreaRulesCreateModel();
    this.newAreaRulesString = '';
    this.newAreaRulesDayOfWeek = null;
    this.newAreaRulesRepeatEvery = 1;
    this.newAreaRulesForType7 = [];
    this.newAreaRulesForType8 = [];
    this.dialogRef.close();
  }

  generateRules() {
    const lines = this.newAreaRulesString.split('\n');
    for (let i = 0; i < lines.length; i++) {
      this.newAreaRules.areaRules = [
        ...this.newAreaRules.areaRules,
        {
          typeSpecificFields: this.generateAreaTypeSpecificFields(),
          translatedNames: this.selectedArea.languages.map((x) => {
            return {name: lines[i], id: x.id, description: x.name};
          }),
        },
      ];
    }
    if (this.selectedArea.type === 10) {
      this.dataForTable = this.getDataForTable();
    }
    // Add weekday for type 4
  }

  generateAreaTypeSpecificFields(): AreaRuleTypeSpecificFields {
    if (this.selectedArea.type === 1) {
      return {
        eformId: this.selectedArea.initialFields.eformId,
        eformName: this.selectedArea.initialFields.eformName,
      };
    }
    if (this.selectedArea.type === 2) {
      return {
        type: AreaRuleT2TypesEnum.Closed,
        alarm: AreaRuleT2AlarmsEnum.No,
      };
    }
    if (this.selectedArea.type === 3) {
      return {
        eformId: this.selectedArea.initialFields.eformId,
        eformName: this.selectedArea.initialFields.eformName,
      };
    }
    if (this.selectedArea.type === 5) {
      return {
        eformId: this.selectedArea.initialFields.eformId,
        eformName: this.selectedArea.initialFields.eformName,
        dayOfWeek: this.newAreaRulesDayOfWeek,
        repeatEvery: this.newAreaRulesRepeatEvery,
      };
    }
    if (this.selectedArea.type === 6) {
      return {
        eformId: 0,
      };
    }
    if (this.selectedArea.type === 10) {
      const poolHoursModel = new PoolHoursModel()
      poolHoursModel.parrings = [];
      for (let i = 0; i < this.days.length; i++) {
        for (let j = 0; j < this.hours.length; j++) {
          poolHoursModel.parrings.push(new PoolHourModel(i, j, false, this.hours[j]));
        }
      }
      return {
        eformId: 0,
        poolHoursModel: poolHoursModel
      };
    }
    return null;
  }

  onCreateAreaRule() {
    if (this.selectedArea.type === 7) {
      const areaRuleNamesForCreate = this.newAreaRulesForType7
        .filter(x => !this.areaRules.some(y => y.translatedName === x));
      const areaRuleIdsForDelete = this.areaRules
        .filter(x => !this.newAreaRulesForType7.some(y => y === x.translatedName))
        .map(x => x.id);
      const areaRulesForCreate = new AreaRulesCreateModel();
      for (let i = 0; i < areaRuleNamesForCreate.length; i++) {
        areaRulesForCreate.areaRules = [
          ...areaRulesForCreate.areaRules,
          {
            typeSpecificFields: {},
            translatedNames: [{name: areaRuleNamesForCreate[i], id: 0, description: ''}]
          },
        ];
      }
      if (areaRulesForCreate.areaRules.length > 0) {
        this.createAreaRule.emit(areaRulesForCreate);
      }
      if (areaRuleIdsForDelete.length > 0) {
        this.deleteAreaRule.emit(areaRuleIdsForDelete);
      }
    } else {
      if (this.selectedArea.type === 8) {
      const areaRuleNamesForCreate = this.newAreaRulesForType8
        .filter(x => !this.areaRules.some(y => y.translatedName === x));
      const areaRuleIdsForDelete = this.areaRules
        .filter(x => !this.newAreaRulesForType8.some(y => y === x.translatedName))
        .map(x => x.id);
      const areaRulesForCreate = new AreaRulesCreateModel();
      for (let i = 0; i < areaRuleNamesForCreate.length; i++) {
        areaRulesForCreate.areaRules = [
          ...areaRulesForCreate.areaRules,
          {
            typeSpecificFields: {},
            translatedNames: [{name: areaRuleNamesForCreate[i], id: 0, description: ''}]
          },
        ];
      }
      if (areaRulesForCreate.areaRules.length > 0) {
        this.createAreaRule.emit(areaRulesForCreate);
      }
      if (areaRuleIdsForDelete.length > 0) {
        this.deleteAreaRule.emit(areaRuleIdsForDelete);
      }
    }
      this.createAreaRule.emit(this.newAreaRules);
    }
  }

  addOrRemoveAreaRuleName(areaRuleName: string, e: boolean) {
    if (this.selectedArea.type === 7) {
      if (e) {
        this.newAreaRulesForType7 = [...this.newAreaRulesForType7, areaRuleName];
      } else {
        this.newAreaRulesForType7 = this.newAreaRulesForType7.filter(x => x !== areaRuleName);
      }
    } else {
      if (e) {
        this.newAreaRulesForType8 = [...this.newAreaRulesForType8, areaRuleName];
      } else {
        this.newAreaRulesForType8 = this.newAreaRulesForType8.filter(x => x !== areaRuleName);
      }
    }
  }

  getChecked(areaRuleName: string): boolean {
    if (this.selectedArea.type === 7) {
      return this.newAreaRulesForType7.find(x => x === areaRuleName) !== undefined;
    } else {
      return this.newAreaRulesForType8.find(x => x === areaRuleName) !== undefined;
    }
  }

  getIsSaveButtonDisabled(): boolean {
    if (this.selectedArea.type === 7) {
      return this.newAreaRulesForType7.length === 0;
    } else {
      if (this.selectedArea.type === 8) {
        return this.newAreaRulesForType8.length === 0;
      } else {
        return this.newAreaRules.areaRules.length === 0;
      }
    }
  }

  getDayByIndex(index: number) {
    return this.days[index];
  }

  getDataForTable() {
    let resultArray = [];
    let resultResultArray = [];
    for (let i = 0; i < this.newAreaRules.areaRules.length; i += 1) {
      const array = [...this.newAreaRules.areaRules[i].typeSpecificFields.poolHoursModel.parrings];
      const chunkSize = 24;
      for (let j = 0; j < array.length; j += chunkSize) {
        const chunk = array.slice(j, j + chunkSize);
        resultArray = [...resultArray, chunk];
      }
    }
    for (let j = 0; j < resultArray.length; j += 7) {
      const chunk = resultArray.slice(j, j + 7);
      resultResultArray = [...resultResultArray, chunk];
    }
    return resultResultArray;
  }
}
