import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Inject,
  OnInit,
  TemplateRef,
  ViewChild,
} from '@angular/core';
import { debounceTime, switchMap } from 'rxjs/operators';
import { TemplateListModel, TemplateRequestModel } from 'src/app/common/models';
import { EFormService } from 'src/app/common/services';
import {
  AreaModel,
  AreaRuleModel,
  AreaRuleUpdateModel,
  PoolHourModel,
} from '../../../../models';
import * as R from 'ramda';
import {TranslateService} from '@ngx-translate/core';
import {MtxGridCellTemplate, MtxGridColumn} from '@ng-matero/extensions/grid';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-area-rule-edit-modal',
  templateUrl: './area-rule-edit-modal.component.html',
  styleUrls: ['./area-rule-edit-modal.component.scss'],
})
export class AreaRuleEditModalComponent implements OnInit {
  @ViewChild('checkboxTpl', { static: true }) checkboxTpl!: TemplateRef<any>;
  @ViewChild('weekdaysTpl', { static: true }) weekdaysTpl!: TemplateRef<any>;
  selectedArea: AreaModel = new AreaModel();
  updateAreaRule: EventEmitter<AreaRuleUpdateModel> = new EventEmitter<AreaRuleUpdateModel>();
  selectedAreaRule: AreaRuleUpdateModel = new AreaRuleUpdateModel();
  planningStatus: boolean;
  typeahead = new EventEmitter<string>();
  templatesModel: TemplateListModel = new TemplateListModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
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
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<AreaRuleEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) model: {areaRule: AreaRuleModel, selectedArea: AreaModel, planningStatus: boolean}
  ) {
    this.planningStatus = model.planningStatus;
    //this.selectedAreaRule = R.clone(model.areaRule);
    this.selectedAreaRule = new AreaRuleUpdateModel();
    this.selectedAreaRule.id = model.areaRule.id;
    this.selectedAreaRule.eformId = model.areaRule.eformId;
    this.selectedAreaRule.eformName = model.areaRule.eformName;
    this.selectedAreaRule.typeSpecificFields = R.clone(model.areaRule.typeSpecificFields);
    this.selectedAreaRule.translatedNames = R.clone(model.areaRule.translatedNames);
    this.selectedArea = model.selectedArea;

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
  }

  ngOnInit() {
    this.hours.forEach((x, index) => {
      this.templates = {...this.templates, [`${index}.isActive`]: this.checkboxTpl};
    });
    this.templates = {...this.templates, weekdays: this.weekdaysTpl};

    this.dataForTable = this.getDataForTable();
  }

  hide() {
    this.selectedAreaRule = new AreaRuleUpdateModel();
    this.dialogRef.close();
  }

  onUpdateAreaRule() {
    this.updateAreaRule.emit(this.selectedAreaRule);
  }

  changeEform(eformId: number) {
    this.selectedAreaRule.eformId = eformId;
    this.selectedAreaRule.eformName = this.templatesModel.templates.find(
      (x) => x.id === eformId
    ).label;
  }

  getDayByIndex(index: number) {
    return this.days[index];
  }

  getDataForTable() {
    let resultArray = [];
    const array = [...this.selectedAreaRule.typeSpecificFields.poolHoursModel.parrings];
    const chunkSize = 24;
    for (let j = 0; j < array.length; j += chunkSize) {
      const chunk = array.slice(j, j + chunkSize);
      resultArray = [...resultArray, chunk];
    }
    return resultArray;
  }
}
