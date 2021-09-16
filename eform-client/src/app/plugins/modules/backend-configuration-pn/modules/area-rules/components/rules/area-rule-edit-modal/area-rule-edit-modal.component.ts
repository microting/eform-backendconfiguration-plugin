import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { AreaRuleModel, AreaRuleUpdateModel } from '../../../../../models';

@Component({
  selector: 'app-area-rule-edit-modal',
  templateUrl: './area-rule-edit-modal.component.html',
  styleUrls: ['./area-rule-edit-modal.component.scss'],
})
export class AreaRuleEditModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() updateAreaRule: EventEmitter<AreaRuleUpdateModel> =
    new EventEmitter<AreaRuleUpdateModel>();
  selectedAreaRule: AreaRuleUpdateModel = new AreaRuleUpdateModel();

  constructor() {}

  ngOnInit() {}

  show(model: AreaRuleModel) {
    this.selectedAreaRule = { ...model };
    this.frame.show();
  }

  hide() {
    this.selectedAreaRule = new AreaRuleUpdateModel();
    this.frame.hide();
  }

  onUpdateAreaRule() {
    this.updateAreaRule.emit(this.selectedAreaRule);
  }
}
