import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { PropertyModel, PropertyUpdateModel } from '../../../../models';

@Component({
  selector: 'app-property-edit',
  templateUrl: './property-edit.component.html',
  styleUrls: ['./property-edit.component.scss'],
})
export class PropertyEditComponent implements OnInit {
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
