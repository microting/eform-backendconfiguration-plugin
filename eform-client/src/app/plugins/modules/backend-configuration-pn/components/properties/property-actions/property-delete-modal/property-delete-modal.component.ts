import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { PropertyModel } from '../../../../models';

@Component({
  selector: 'app-property-delete-modal',
  templateUrl: './property-delete-modal.component.html',
  styleUrls: ['./property-delete-modal.component.scss'],
})
export class PropertyDeleteModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() propertyDelete: EventEmitter<number> = new EventEmitter<number>();
  propertyModel: PropertyModel = new PropertyModel();

  constructor() {}

  ngOnInit() {}

  show(propertyModel: PropertyModel) {
    this.propertyModel = propertyModel;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  deleteProperty() {
    this.propertyDelete.emit(this.propertyModel.id);
  }
}
