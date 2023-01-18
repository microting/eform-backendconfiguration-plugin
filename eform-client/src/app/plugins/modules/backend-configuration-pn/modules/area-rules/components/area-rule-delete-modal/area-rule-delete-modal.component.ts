import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
import {AreaRuleSimpleModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-area-rule-delete-modal',
  templateUrl: './area-rule-delete-modal.component.html',
  styleUrls: ['./area-rule-delete-modal.component.scss']
})
export class AreaRuleDeleteModalComponent implements OnInit {
  deleteAreaRule: EventEmitter<number> = new EventEmitter<number>();

  constructor(
    public dialogRef: MatDialogRef<AreaRuleDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public areaRule: AreaRuleSimpleModel = new AreaRuleSimpleModel(),
  ) {
  }

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
  }

  onDeleteAreaRule() {
    this.deleteAreaRule.emit(this.areaRule.id);
  }
}
