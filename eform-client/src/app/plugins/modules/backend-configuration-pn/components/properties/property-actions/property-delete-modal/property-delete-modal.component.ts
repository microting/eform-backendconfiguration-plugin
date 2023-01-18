import {
  Component,
  EventEmitter,
  Inject,
  OnInit,
} from '@angular/core';
import { PropertyModel } from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-property-delete-modal',
  templateUrl: './property-delete-modal.component.html',
  styleUrls: ['./property-delete-modal.component.scss'],
})
export class PropertyDeleteModalComponent implements OnInit {
  propertyDelete: EventEmitter<number> = new EventEmitter<number>();

  constructor(
    public dialogRef: MatDialogRef<PropertyDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public propertyModel: PropertyModel = new PropertyModel()
  ) {}

  ngOnInit() {}

  hide() {
    this.dialogRef.close();
  }

  deleteProperty() {
    this.propertyDelete.emit(this.propertyModel.id);
  }
}
