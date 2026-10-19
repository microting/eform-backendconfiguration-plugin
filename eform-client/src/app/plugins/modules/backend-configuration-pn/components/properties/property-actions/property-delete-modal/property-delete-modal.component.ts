import {
  Component,
  EventEmitter,
  OnInit,
  inject
} from '@angular/core';
import { PropertyModel } from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';

@Component({
    selector: 'app-property-delete-modal',
    imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
    templateUrl: './property-delete-modal.component.html',
    styleUrls: ['./property-delete-modal.component.scss'],
})
export class PropertyDeleteModalComponent implements OnInit {
  public dialogRef = inject(MatDialogRef<PropertyDeleteModalComponent>);
  public propertyModel = inject<PropertyModel>(MAT_DIALOG_DATA);

  propertyDelete: EventEmitter<number> = new EventEmitter<number>();

  

  ngOnInit() {}

  hide() {
    this.dialogRef.close();
  }

  deleteProperty() {
    this.propertyDelete.emit(this.propertyModel.id);
  }
}
