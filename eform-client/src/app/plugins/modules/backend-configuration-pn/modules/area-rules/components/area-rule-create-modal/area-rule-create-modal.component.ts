import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
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
  AreaModel, AreaRuleCreateModel,
  AreaRulesCreateModel, AreaRuleSimpleModel,
  AreaRuleTypeSpecificFields,
} from '../../../../models';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {PoolHourModel, PoolHoursModel} from 'src/app/plugins/modules/backend-configuration-pn/models/pools/pool-hour.model';

@Component({
  selector: 'app-area-rule-create-modal',
  templateUrl: './area-rule-create-modal.component.html',
  styleUrls: ['./area-rule-create-modal.component.scss'],
})
export class AreaRuleCreateModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @Input() areaRules: AreaRuleSimpleModel[] = [];
  @ViewChild('frame', {static: false}) frame;
  @Output()
  createAreaRule: EventEmitter<AreaRulesCreateModel> = new EventEmitter<AreaRulesCreateModel>();
  @Output()
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

  constructor(
    private eFormService: EFormService,
    private cd: ChangeDetectorRef,
    private backendConfigurationPnAreasService: BackendConfigurationPnAreasService
  ) {
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
  }

  show() {
    if (this.selectedArea.type === 7) {
      this.backendConfigurationPnAreasService
        .getAreaRulesForType7()
        .subscribe((data) => {
          if (data.success) {
            this.areaRulesForType7 = data.model;
          }
        });
      this.areaRules.forEach(x => this.newAreaRulesForType7 = [...this.newAreaRulesForType7, x.translatedName]);
      this.frame.show();
    } else {
      if (this.selectedArea.type === 8) {
        this.backendConfigurationPnAreasService
          .getAreaRulesForType8()
          .subscribe((data) => {
            if (data.success) {
              this.areaRulesForType8 = data.model;
            }
          });
        this.areaRules.forEach(x => this.newAreaRulesForType8 = [...this.newAreaRulesForType8, x.translatedName]);
        this.frame.show();
      } else {
        this.frame.show();
      }
    }
  }

  hide() {
    this.newAreaRules = new AreaRulesCreateModel();
    this.newAreaRulesString = '';
    this.newAreaRulesDayOfWeek = null;
    this.newAreaRulesRepeatEvery = 1;
    this.newAreaRulesForType7 = [];
    this.newAreaRulesForType8 = [];
    this.frame.hide();
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

  addOrRemoveAreaRuleName(areaRuleName: string, e: any) {
    if (this.selectedArea.type === 7) {
      if (e.target.checked) {
        this.newAreaRulesForType7 = [...this.newAreaRulesForType7, areaRuleName];
      } else {
        this.newAreaRulesForType7 = this.newAreaRulesForType7.filter(x => x !== areaRuleName);
      }
    } else {
      if (e.target.checked) {
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

  checked($event: any, i: number, j: number, areaRule: AreaRuleCreateModel) {
    for (let k = 0; k < areaRule.typeSpecificFields.poolHoursModel.parrings.length; k++) {
      if (areaRule.typeSpecificFields.poolHoursModel.parrings[k].dayOfWeek === i && areaRule.typeSpecificFields.poolHoursModel.parrings[k].index === j) {
        areaRule.typeSpecificFields.poolHoursModel.parrings[k].isActive = $event.target.checked;
      }
    }
  }
}
