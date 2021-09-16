import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {AreaRuleSimpleModel} from '../../../../../models';

@Component({
  selector: 'app-area-rule-delete-modal',
  templateUrl: './area-rule-delete-modal.component.html',
  styleUrls: ['./area-rule-delete-modal.component.scss']
})
export class AreaRuleDeleteModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() deleteAreaRule: EventEmitter<number> = new EventEmitter<number>();
  areaRule: AreaRuleSimpleModel = new AreaRuleSimpleModel();

  constructor() {}

  ngOnInit() {}

  show(rule: AreaRuleSimpleModel) {
    this.areaRule = rule;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  onDeleteAreaRule() {
    this.deleteAreaRule.emit(this.areaRule.id);
  }
}
