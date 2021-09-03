import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { PropertyCreateModel } from '../../../../models';

@Component({
  selector: 'app-property-create-modal',
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
})
export class PropertyCreateModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() propertyCreate: EventEmitter<PropertyCreateModel> =
    new EventEmitter<PropertyCreateModel>();
  newProperty: PropertyCreateModel = new PropertyCreateModel();

  constructor() {}

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.newProperty = new PropertyCreateModel();
    this.frame.hide();
  }

  onCreateProperty() {
    this.propertyCreate.emit(this.newProperty);
  }
}
