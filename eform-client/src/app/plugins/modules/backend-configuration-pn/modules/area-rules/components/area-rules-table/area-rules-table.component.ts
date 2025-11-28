import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges, OnInit,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import {Paged,} from 'src/app/common/models';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from '../../../../enums';
import {AreaModel, ChemicalModel, AreaRuleSimpleModel} from '../../../../models';
import {Subscription} from 'rxjs';
import {TemplateFilesService} from 'src/app/common/services';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {PdfIcon} from 'src/app/common/const';
import {AuthStateService} from 'src/app/common/store';
import {AreaRulesStateService} from '../store';
import {Sort} from '@angular/material/sort';
import * as R from 'ramda';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {
  selectAreaRulesPaginationIsSortDsc,
  selectAreaRulesPaginationSort
} from "src/app/plugins/modules/backend-configuration-pn/state/area-rules/area-rules.selector";

@Component({
    selector: 'app-area-rules-table',
    templateUrl: './area-rules-table.component.html',
    styleUrls: ['./area-rules-table.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    standalone: false
})
export class AreaRulesTableComponent implements OnChanges, OnInit {
  private store = inject(Store);
  private authStateService = inject(AuthStateService);
  private templateFilesService = inject(TemplateFilesService);
  private translateService = inject(TranslateService);
  public areaRulesStateService = inject(AreaRulesStateService);
  private iconRegistry = inject(MatIconRegistry);
  private sanitizer = inject(DomSanitizer);

  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @Input() chemicalsModel: Paged<ChemicalModel> = new Paged<ChemicalModel>();
  @Input() selectedArea: AreaModel = new AreaModel();
  @Output()
  showPlanAreaRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showEditRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showDeleteRuleModal: EventEmitter<AreaRuleSimpleModel> = new EventEmitter();
  @Output()
  showEditEntityListModal: EventEmitter<number> = new EventEmitter();
  @Output() sortTable: EventEmitter<Sort> = new EventEmitter<Sort>();

  tableItemsForAreaRulesDefaultT3: AreaRuleSimpleModel[] = [];
  tableItemsForAreaRulesDefaultT10b: AreaRuleSimpleModel[] = [];

  pdfSub$: Subscription;

  tableHeadersT1: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
      sortable: true,
      sortProp: {id: 'Id'},
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
      sortable: true,
      sortProp: {id: 'TranslatedName'},
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
      sortable: true,
      sortProp: {id: 'EformName'},
    },
    {
      field: 'startDate',
      header: this.translateService.stream('Start date'),
      sortable: true,
      sortProp: {id: 'StartDate'},
      type: 'date',
      typeParameter: {format: 'dd.MM.y HH:mm:ss'},
    },
    {
      field: 'repeatType',
      header: this.translateService.stream('Repeat type'),
      sortable: true,
      sortProp: {id: 'RepeatType'},
      formatter: (row: AreaRuleSimpleModel) => {
        const callback = (x: { id: number, name: string }) => x.id === row.repeatType;
        if (row.repeatType && this.repeatTypeArr.some(callback)) {
          const retValue = this.translateService.instant(this.repeatTypeArr.find(callback).name);
          return `<span title="${retValue}">${retValue}</span>`;
        }
        return '--';
      }
    },
    {
      field: 'repeatEvery',
      header: this.translateService.stream('Repeat Every'),
      sortable: true,
      sortProp: {id: 'RepeatEvery'},
      formatter: (row: AreaRuleSimpleModel) => {
        let masForFind = [];
        switch (row.repeatType){
          case 1: {
            masForFind = this.repeatEveryTypeDay;
            break;
          }
          case 2: {
            masForFind = this.repeatEveryTypeWeek;
            break;
          }
          case 3: {
            masForFind = this.repeatEveryTypeMonth;
            break;
          }
          default: {
            masForFind = [];
          }
        }
        const callback = (x: { id: number, name: string }) => x.id === row.repeatEvery;
        if (row.repeatEvery && masForFind.some(callback)) {
          const retValue = this.translateService.instant(masForFind.find(callback).name);
          return `<span title="${retValue}">${retValue}</span>`;
        }
        return '--';
      }
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus',
      sortable: true,
      sortProp: {id: 'PlanningStatus'},
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT2: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    // {
    //   field: 'type',
    //   header: this.translateService.stream('Type'),
    //   class: 'ruleType',
    //   formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(AreaRuleT2TypesEnum[rowData.typeSpecificFields.type]),
    //   //formatter: (rowData: AreaRuleSimpleModel) => AreaRuleT2TypesEnum[rowData.typeSpecificFields.type],
    // },
    // {
    //   field: 'alarm',
    //   header: this.translateService.stream('Alarm'),
    //   class: 'alarm',
    //   formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(AreaRuleT2AlarmsEnum[rowData.typeSpecificFields.alarm]),
    // },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT3: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT4: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT5: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'weekDay',
      header: this.translateService.stream('Week Day'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(this.getWeekDayName(rowData)),
      class: 'ruleWeekDay',
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT6: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT7: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT9: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'eformName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT9SecondTable: MtxGridColumn[] = [
    {
      field: 'name',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'registrationNo',
      header: this.translateService.stream('Registration No'),
    },
    {
      field: 'status',
      header: this.translateService.stream('Status'),
      formatter: (chemical: ChemicalModel) => this.getStatus(chemical.status),
      class: 'rulePlanningStatus'
    },
    {
      field: 'propertyName',
      header: this.translateService.stream('Property'),
    },
    {
      field: 'locations',
      header: this.translateService.stream('Rum'),
    },
    {
      field: 'expiredState',
      header: this.translateService.stream('Expire state'),
    },
    {
      field: 'expiredDate',
      header: this.translateService.stream('Expiration Date'),
    },
    {
      field: 'pdf',
      header: this.translateService.stream('PDF'),
    },
  ];

  tableHeadersT10: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'translatedName',
      header: this.translateService.stream('Name'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeadersT10b_: MtxGridColumn[] = [
    {
      field: 'id',
      header: this.translateService.stream('ID'),
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
      class: 'rulePlanningStatus'
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
    },
  ];

  tableHeaderAdmin: MtxGridColumn[] = [
    {
      field: 'createdAt',
      header: this.translateService.stream('Creation Date'),
      type: 'date',
      typeParameter: {format: 'dd.MM.y HH:mm:ss'},
    },
    {
      field: 'updatedAt',
      header: this.translateService.stream('Updated At'),
      type: 'date',
      typeParameter: {format: 'dd.MM.y HH:mm:ss'},
    }
  ];
  repeatTypeArr: { id: number; name: string; }[] = [];
  repeatEveryTypeWeek: { id: number; name: string; }[] = [];
  repeatEveryTypeMonth: { id: number; name: string; }[] = [];
  repeatEveryTypeDay: { id: number; name: string; }[] = [];
  private selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);

  getColumns(): MtxGridColumn[] {
    let isAdmin = false;
    this.selectAuthIsAdmin$.subscribe((selectAuthIsAdmin$) => isAdmin = selectAuthIsAdmin$);
    if (!isAdmin) {
      this.tableHeaderAdmin = [];
    }

    switch (this.selectedArea.type) {
      case 1:
        return [...this.tableHeadersT1, ...this.tableHeaderAdmin];
      case 2:
        return [...this.tableHeadersT2, ...this.tableHeaderAdmin];
      case 3:
        return [...this.tableHeadersT3, ...this.tableHeaderAdmin];
      case 4:
        return [...this.tableHeadersT4, ...this.tableHeaderAdmin];
      case 5:
        return [...this.tableHeadersT5, ...this.tableHeaderAdmin];
      case 6:
        return [...this.tableHeadersT6, ...this.tableHeaderAdmin];
      case 7:
        return [...this.tableHeadersT7, ...this.tableHeaderAdmin];
      case 8:
        return [...this.tableHeadersT7, ...this.tableHeaderAdmin];
      case 9:
        return [...this.tableHeadersT9, ...this.tableHeaderAdmin];
      case 10:
        return [...this.tableHeadersT10, ...this.tableHeaderAdmin];
      default:
        return [];
    }
  }
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectAreaRulesPaginationSort$ = this.store.select(selectAreaRulesPaginationSort);
  public selectAreaRulesPaginationIsSortDsc$ = this.store.select(selectAreaRulesPaginationIsSortDsc);

  
  constructor() {
    this.iconRegistry.addSvgIconLiteral('file-pdf', this.sanitizer.bypassSecurityTrustHtml(PdfIcon));
  }


  ngOnChanges(changes: SimpleChanges): void {
    if (
      (
        (changes.selectedArea && !changes.selectedArea.firstChange) ||
        (changes.areaRules && !changes.areaRules.firstChange)
      ) &&
      this.areaRules &&
      this.selectedArea &&
      (this.selectedArea.type === 3 || this.selectedArea.type === 10)
    ) {
      this.tableItemsForAreaRulesDefaultT3 = this.areaRules.filter(x => x.isDefault);
      this.tableItemsForAreaRulesDefaultT10b = this.areaRules
        .filter(rule => rule.secondaryeFormId !== 0 || rule.translatedName === 'Morgenrundtur' || rule.translatedName === 'Morning tour');
      this.areaRules = this.areaRules
        .filter(x => !x.isDefault)
        .filter(rule => rule.secondaryeFormId === 0 && rule.secondaryeFormName !== 'Morgenrundtur');
    }
  }

  onShowPlanAreaRule(rule: AreaRuleSimpleModel) {
    this.showPlanAreaRuleModal.emit(rule);
  }

  onShowEditRuleModal(rule: AreaRuleSimpleModel) {
    this.showEditRuleModal.emit(rule);
  }

  onShowDeleteRuleModal(rule: AreaRuleSimpleModel) {
    this.showDeleteRuleModal.emit(rule);
  }

  getWeekDayName(areaRule: AreaRuleSimpleModel): string {
    const weekNames = [
      {id: 1, name: 'Monday'},
      {id: 2, name: 'Tuesday'},
      {id: 3, name: 'Wednesday'},
      {id: 4, name: 'Thursday'},
      {id: 5, name: 'Friday'},
      {id: 6, name: 'Saturday'},
      {id: 0, name: 'Sunday'},
    ];
    return weekNames
      .find((y) => y.id === areaRule.typeSpecificFields.dayOfWeek).name;
  }

  onShowEditEntityListModal(groupId?: number) {
    this.showEditEntityListModal.emit(groupId);
  }

  onSortTable(sort: Sort) {
    this.sortTable.emit(sort);
  }

  getPdf(fileName: string) {
    // TODO: CHECK
    this.pdfSub$ = this.templateFilesService.getPdfFile(fileName).subscribe((blob) => {
      const fileURL = URL.createObjectURL(blob);
      window.open(fileURL, '_blank');
    });
  }

  getStatus(status: number) {
    switch (status) {
      case 1:
        return 'Ansøgning om nyt produkt modtaget';
      case 2:
        return 'Ansøgning om nyt produkt trukket';
      case 3:
        return 'Ansøgning om nyt produkt returneret';
      case 4:
        return 'Ansøgning om nyt produkt afslået';
      case 5:
        return 'Produkt godkendt';
      case 6:
        return 'Produkt afmeldt';
      case 7:
        return 'Produkt udløbet';
      case 8:
        return 'Produkt afslået';
      case 9:
        return 'Ansøgning om nyt produkt annulleret';
      default:
        return '';
    }
  }

  ngOnInit(): void {
    this.repeatTypeArr = [
      {id: 1, name: 'Day'},
      {id: 2, name: 'Week'},
      {id: 3, name: 'Month'},
    ];
    this.repeatEveryTypeDay = R.map(x => {
      return {name: x === 1 ? this.translateService.instant('Every') : x.toString(), id: x};
    }, R.range(1, 31)); //1, 2, ..., 29, 30.
    this.repeatEveryTypeWeek = [
      {id: 1, name: 'Weekly'},
      {id: 2, name: '2nd week'},
      {id: 3, name: '3rd week'},
      {id: 4, name: '4th week'},
      {id: 5, name: '5th week'},
      {id: 6, name: '6th week'},
      {id: 7, name: '7th week'},
      {id: 8, name: '8th week'},
      {id: 9, name: '9th week'},
      {id: 10, name: '10th week'},
      {id: 11, name: '11st week'},
      {id: 12, name: '12nd week'},
      {id: 13, name: '13rd week'},
      {id: 14, name: '14th week'},
      {id: 15, name: '15th week'},
      {id: 16, name: '16th week'},
      {id: 17, name: '17th week'},
      {id: 18, name: '18th week'},
      {id: 19, name: '19th week'},
      {id: 20, name: '20th week'},
      {id: 21, name: '21st week'},
      {id: 22, name: '22nd week'},
      {id: 23, name: '23rd week'},
      {id: 24, name: '24th week'},
      {id: 25, name: '25th week'},
      {id: 26, name: '26th week'},
      {id: 27, name: '27th week'},
      {id: 28, name: '28th week'},
      {id: 29, name: '29th week'},
      {id: 30, name: '30th week'},
      {id: 31, name: '31st week'},
      {id: 32, name: '32nd week'},
      {id: 33, name: '33rd week'},
      {id: 34, name: '34th week'},
      {id: 35, name: '35th week'},
      {id: 36, name: '36th week'},
      {id: 37, name: '37th week'},
      {id: 38, name: '38th week'},
      {id: 39, name: '39th week'},
      {id: 40, name: '40th week'},
      {id: 41, name: '41st week'},
      {id: 42, name: '42nd week'},
      {id: 43, name: '43rd week'},
      {id: 44, name: '44th week'},
      {id: 45, name: '45th week'},
      {id: 46, name: '46th week'},
      {id: 47, name: '47th week'},
      {id: 48, name: '48th week'},
      {id: 49, name: '49th week'},
      {id: 50, name: '50th week'},
      {id: 51, name: '51st week'},
      {id: 52, name: '52nd week'},
    ];
    this.repeatEveryTypeMonth = [
      {id: 1, name: this.translateService.instant('Every month')},
      {id: 2, name: this.translateService.instant('2nd months')},
      {id: 3, name: this.translateService.instant('3rd months')},
      {id: 6, name: this.translateService.instant('6th months')},
      {id: 12, name: this.translateService.instant('12 (1 year)')},
      {id: 24, name: this.translateService.instant('24 (2 years)')},
      {id: 36, name: this.translateService.instant('36 (3 years)')},
      {id: 48, name: this.translateService.instant('48 (4 years)')},
      {id: 60, name: this.translateService.instant('60 (5 years)')},
      {id: 72, name: this.translateService.instant('72 (6 years)')},
      {id: 84, name: this.translateService.instant('84 (7 years)')},
      {id: 96, name: this.translateService.instant('96 (8 years)')},
      {id: 108, name: this.translateService.instant('108 (9 years)')},
      {id: 120, name: this.translateService.instant('120 (10 years)')},
    ];
  }
}
