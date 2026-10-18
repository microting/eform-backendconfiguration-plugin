import {Component, EventEmitter, OnInit,
  inject
} from '@angular/core';
import {AreaRuleSimpleModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';

@Component({
    selector: 'app-area-rule-delete-modal',
    imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
    templateUrl: './area-rule-delete-modal.component.html',
    styleUrls: ['./area-rule-delete-modal.component.scss'],
})
export class AreaRuleDeleteModalComponent implements OnInit {
  public dialogRef = inject(MatDialogRef<AreaRuleDeleteModalComponent>);
  public areaRule = inject<AreaRuleSimpleModel>(MAT_DIALOG_DATA);

  deleteAreaRule: EventEmitter<number> = new EventEmitter<number>();

  

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
  }

  onDeleteAreaRule() {
    this.deleteAreaRule.emit(this.areaRule.id);
  }
}
