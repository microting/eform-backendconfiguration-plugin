import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { applicationLanguages } from 'src/app/common/const';
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
} from '../../../../../models';

@Component({
  selector: 'app-area-rule-create-modal',
  templateUrl: './area-rule-create-modal.component.html',
  styleUrls: ['./area-rule-create-modal.component.scss'],
})
export class AreaRuleCreateModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @ViewChild('frame', { static: false }) frame;
  @Output() createAreaRule: EventEmitter<AreaRulesCreateModel> =
    new EventEmitter<AreaRulesCreateModel>();
  newAreaRules: AreaRulesCreateModel = new AreaRulesCreateModel();
  newAreaRulesString: string;

  constructor() {}

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.newAreaRules = new AreaRulesCreateModel();
    this.newAreaRulesString = '';
    this.frame.hide();
  }

  generateRules() {
    const lines = this.newAreaRulesString.split('\n');
    for (let i = 0; i < lines.length; i++) {
      this.newAreaRules.areaRules = [
        ...this.newAreaRules.areaRules,
        {
          typeSpecificFields: this.generateAreaTypeSpecificFields(),
          translatedNames: applicationLanguages.map((x) => {
            return { value: lines[i], languageId: x.id };
          }),
        },
      ];
    }
  }

  generateAreaTypeSpecificFields():
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model {
    if (this.selectedArea.type === 1) {
      return { eformId: null };
    }
    if (this.selectedArea.type === 2) {
      return {
        type: AreaRuleT2TypesEnum.Closed,
        alarm: AreaRuleT2AlarmsEnum.No,
      };
    }
    if (this.selectedArea.type === 3) {
      return { checklistStable: false, tailBite: false, eformId: null };
    }
    return null;
  }

  onCreateAreaRule() {
    this.createAreaRule.emit(this.newAreaRules);
  }
}
