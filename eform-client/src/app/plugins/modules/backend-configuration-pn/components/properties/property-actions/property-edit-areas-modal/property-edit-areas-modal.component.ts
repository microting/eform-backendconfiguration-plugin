import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {PropertyModel, PropertyUpdateModel} from '../../../../models';
import {PropertyAreasUpdateModel} from '../../../../models';

@Component({
  selector: 'app-property-edit-areas-modal',
  templateUrl: './property-edit-areas-modal.component.html',
  styleUrls: ['./property-edit-areas-modal.component.scss']
})
export class PropertyEditAreasModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() updatePropertyAreas: EventEmitter<PropertyAreasUpdateModel> =
    new EventEmitter<PropertyAreasUpdateModel>();
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

  onUpdatePropertyAreas() {
    // this.updatePropertyAreas.emit(this.selectedProperty);
  }
}
