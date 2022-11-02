import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
} from '@angular/core';
import {Paged,} from 'src/app/common/models';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from '../../../../enums';
import {AreaModel,ChemicalModel, AreaRuleSimpleModel} from '../../../../models';
import {Subscription} from 'rxjs';
import {TemplateFilesService} from 'src/app/common/services';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import { PdfIcon } from 'src/app/common/const';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaRulesTableComponent implements OnChanges {
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
  @Output() sortTable: EventEmitter<string> = new EventEmitter<string>();

  tableItemsForAreaRulesDefaultT3: AreaRuleSimpleModel[] = [];
  tableItemsForAreaRulesDefaultT10b: AreaRuleSimpleModel[] = [];

  pdfSub$: Subscription;

  tableHeadersT1: MtxGridColumn[] = [
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
    {
      field: 'type',
      header: this.translateService.stream('Type'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(AreaRuleT2TypesEnum[rowData.typeSpecificFields.type]),
    },
    {
      field: 'alarm',
      header: this.translateService.stream('Alarm'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(AreaRuleT2AlarmsEnum[rowData.typeSpecificFields.alarm]),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
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
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      type: 'button',
      buttons: [
        {
          type: 'icon',
          color: 'accent',
          icon: 'assignment',
          click: (rowData: AreaRuleSimpleModel) => this.onShowPlanAreaRule(rowData),
          tooltip: this.translateService.stream('Plan and assign'),
        },
        {
          type: 'icon',
          color: 'accent',
          icon: 'list',
          click: () => this.onShowEditEntityListModal(this.selectedArea.groupId),
          tooltip: this.translateService.stream('Edit list of stables for tailbites'),
        },
      ]
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
    },
    {
      field: 'eformName',
      header: this.translateService.stream('eForm'),
    },
    {
      field: 'planningStatus',
      header: this.translateService.stream('Status'),
      formatter: (rowData: AreaRuleSimpleModel) => this.translateService.instant(rowData.planningStatus ? 'ON' : 'OFF'),
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
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      type: 'button',
      buttons: [
        {
          type: 'icon',
          color: 'accent',
          icon: 'assignment',
          click: (rowData: AreaRuleSimpleModel) => this.onShowPlanAreaRule(rowData),
          tooltip: this.translateService.stream('Plan and assign'),
        },
        {
          type: 'icon',
          color: 'accent',
          icon: 'edit',
          iif: (rowData: AreaRuleSimpleModel) => !rowData.isDefault && this.selectedArea.type !== 9,
          click: (rowData: AreaRuleSimpleModel) => this.onShowEditRuleModal(rowData),
          tooltip: this.translateService.stream('Edit rule'),
        },
      ]
    },
  ];

  getColumns(): MtxGridColumn[] {
    switch (this.selectedArea.type) {
      case 1:
        return this.tableHeadersT1;
      case 2:
        return this.tableHeadersT2;
      case 3:
        return this.tableHeadersT3;
      case 4:
        return this.tableHeadersT4;
      case 5:
        return this.tableHeadersT5;
      case 6:
        return this.tableHeadersT6;
      case 7:
        return this.tableHeadersT7;
      case 8:
        return this.tableHeadersT7;
      case 9:
        return this.tableHeadersT9;
      case 10:
        return this.tableHeadersT10;
      default:
        return [];
    }
  }

  constructor(
    private templateFilesService: TemplateFilesService,
    private translateService: TranslateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
  ) {
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
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
        .filter(rule => rule.secondaryeFormId !== 0 || rule.translatedName === 'Morgenrundtur' || rule.translatedName === 'Morning tour')
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

  onSortTable(sort: string) {
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
}
