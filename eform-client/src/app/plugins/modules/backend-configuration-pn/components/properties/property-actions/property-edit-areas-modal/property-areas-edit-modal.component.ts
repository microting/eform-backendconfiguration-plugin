import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {
  PropertyAreaModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import { PropertyAreasUpdateModel } from '../../../../models';
import {AuthStateService} from 'src/app/common/store';

@Component({
  selector: 'app-property-edit-areas-modal',
  templateUrl: './property-areas-edit-modal.component.html',
  styleUrls: ['./property-areas-edit-modal.component.scss'],
})
export class PropertyAreasEditModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output()
  updatePropertyAreas: EventEmitter<PropertyAreasUpdateModel> = new EventEmitter<PropertyAreasUpdateModel>();
  selectedProperty: PropertyUpdateModel = new PropertyUpdateModel();
  selectedPropertyAreas: PropertyAreaModel[] = [];

  constructor(
    public authStateService: AuthStateService) {}

  ngOnInit() {}

  getAreaPlanningStatus(area: PropertyAreaModel) {
    return area.status ? 'ON' : 'OFF';
  }

  show(model: PropertyModel, propertyAreas: PropertyAreaModel[]) {
    this.selectedProperty = { ...model, languagesIds: [] };
    this.selectedPropertyAreas = [...propertyAreas];
    this.frame.show();
  }

  hide() {
    this.selectedProperty = new PropertyUpdateModel();
    this.frame.hide();
  }

  onUpdatePropertyAreas() {
    this.updatePropertyAreas.emit({
      propertyId: this.selectedProperty.id,
      areas: this.selectedPropertyAreas,
    });
  }

  updateArea($event: any, area: PropertyAreaModel) {
    const findetArea = this.selectedPropertyAreas.find(
      (x) => x.name === area.name && x.description === area.description
    );
    if (findetArea) {
      findetArea.activated = $event.target.checked;
    }
  }
}
