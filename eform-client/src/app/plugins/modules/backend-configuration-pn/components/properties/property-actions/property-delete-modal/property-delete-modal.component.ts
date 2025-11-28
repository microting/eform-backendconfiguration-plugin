import {
  Component,
  EventEmitter,
  OnInit,
  inject
} from '@angular/core';
import { PropertyModel } from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-property-delete-modal',
    templateUrl: './property-delete-modal.component.html',
    styleUrls: ['./property-delete-modal.component.scss'],
    standalone: false
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
