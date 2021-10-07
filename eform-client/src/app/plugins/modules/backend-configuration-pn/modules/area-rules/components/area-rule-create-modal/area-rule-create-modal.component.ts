import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { debounceTime, switchMap } from 'rxjs/operators';
import { applicationLanguages } from 'src/app/common/const';
import { TemplateListModel, TemplateRequestModel } from 'src/app/common/models';
import { EFormService } from 'src/app/common/services';
import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {
  AreaModel,
  AreaRuleCreateModel,
  AreaRulesCreateModel,
  AreaRuleT1Model,
  AreaRuleT2Model,
  AreaRuleT3Model,
  AreaRuleT5Model,
} from '../../../../models';

@Component({
  selector: 'app-area-rule-create-modal',
  templateUrl: './area-rule-create-modal.component.html',
  styleUrls: ['./area-rule-create-modal.component.scss'],
})
export class AreaRuleCreateModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @ViewChild('frame', { static: false }) frame;
  @Output()
  createAreaRule: EventEmitter<AreaRulesCreateModel> = new EventEmitter<AreaRulesCreateModel>();
  newAreaRules: AreaRulesCreateModel = new AreaRulesCreateModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
  newAreaRulesString: string;
  newAreaRulesDayOfWeek: number | null;
  typeahead = new EventEmitter<string>();
  templatesModel: TemplateListModel = new TemplateListModel();

  constructor(
    private eFormService: EFormService,
    private cd: ChangeDetectorRef
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

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.newAreaRules = new AreaRulesCreateModel();
    this.newAreaRulesString = '';
    this.newAreaRulesDayOfWeek = null;
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
            return { name: lines[i], id: x.id, description: x.name };
          }),
        },
      ];
    }
    // Add weekday for type 4
  }

  generateAreaTypeSpecificFields():
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model
    | AreaRuleT5Model {
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
      return { dayOfWeek: this.newAreaRulesDayOfWeek };
    }
    return null;
  }

  onCreateAreaRule() {
    this.createAreaRule.emit(this.newAreaRules);
  }
}
