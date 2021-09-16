import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {
  AreaRuleCreateModel,
  AreaRulesCreateModel,
} from '../../../../../models';

@Component({
  selector: 'app-area-rule-create-modal',
  templateUrl: './area-rule-create-modal.component.html',
  styleUrls: ['./area-rule-create-modal.component.scss'],
})
export class AreaRuleCreateModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() createAreaRule: EventEmitter<AreaRulesCreateModel> =
    new EventEmitter<AreaRulesCreateModel>();
  newAreaRule: AreaRuleCreateModel = new AreaRuleCreateModel();

  constructor() {}

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.newAreaRule = new AreaRuleCreateModel();
    this.frame.hide();
  }

  onCreateAreaRule() {
    this.createAreaRule.emit({ areaRules: [this.newAreaRule] });
  }
}
