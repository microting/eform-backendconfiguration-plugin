import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { PropertyModel, PropertyUpdateModel } from '../../../../models';

@Component({
  selector: 'app-property-edit-modal',
  templateUrl: './property-edit-modal.component.html',
  styleUrls: ['./property-edit-modal.component.scss'],
})
export class PropertyEditModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() propertyUpdate: EventEmitter<PropertyUpdateModel> =
    new EventEmitter<PropertyUpdateModel>();
  selectedProperty: PropertyUpdateModel = new PropertyUpdateModel();

  constructor() {}

  ngOnInit() {}

  show(model: PropertyModel) {
    this.selectedProperty = { ...model };
    this.frame.show();
  }

  hide() {
    this.selectedProperty = new PropertyUpdateModel();
    this.frame.hide();
  }

  onUpdateProperty() {
    this.propertyUpdate.emit(this.selectedProperty);
  }
}
