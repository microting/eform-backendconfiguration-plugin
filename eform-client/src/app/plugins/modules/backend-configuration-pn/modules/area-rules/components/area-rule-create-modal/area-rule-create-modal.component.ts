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
  AreaModel,
  AreaRulesCreateModel, AreaRuleSimpleModel,
  AreaRuleTypeSpecificFields,
} from '../../../../models';
import {BackendConfigurationPnAreasService} from '../../../../services';

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
  newAreaRulesForType7: string[] = [];

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
      this.frame.show();
    }
  }

  hide() {
    this.newAreaRules = new AreaRulesCreateModel();
    this.newAreaRulesString = '';
    this.newAreaRulesDayOfWeek = null;
    this.newAreaRulesRepeatEvery = 1;
    this.newAreaRulesForType7 = [];
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
        checklistStable: true,
        tailBite: true,
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
      this.createAreaRule.emit(this.newAreaRules);
    }
  }

  addOrRemoveAreaRuleName(areaRuleName: string, e: Event) {
    // @ts-ignore
    if (e.target.checked) {
      this.newAreaRulesForType7 = [...this.newAreaRulesForType7, areaRuleName];
    } else {
      this.newAreaRulesForType7 = this.newAreaRulesForType7.filter(x => x !== areaRuleName);
    }
  }

  getChecked(areaRuleName: string): boolean {
    return this.newAreaRulesForType7.find(x => x === areaRuleName) !== undefined;
  }

  getIsSaveButtonDisabled(): boolean {
    if (this.selectedArea.type === 7) {
      return this.newAreaRulesForType7.length === 0;
    } else {
      return this.newAreaRules.areaRules.length === 0;
    }
  }
}
