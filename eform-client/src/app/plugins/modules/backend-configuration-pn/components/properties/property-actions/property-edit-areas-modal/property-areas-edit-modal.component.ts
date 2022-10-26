import {
  Component,
  EventEmitter,
  Inject,
  OnInit,
} from '@angular/core';
import {
  PropertyAreaModel,
  PropertyModel,
  PropertyUpdateModel,
} from '../../../../models';
import {PropertyAreasUpdateModel} from '../../../../models';
import {AuthStateService} from 'src/app/common/store';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-property-edit-areas-modal',
  templateUrl: './property-areas-edit-modal.component.html',
  styleUrls: ['./property-areas-edit-modal.component.scss'],
})
export class PropertyAreasEditModalComponent implements OnInit {
  updatePropertyAreas: EventEmitter<PropertyAreasUpdateModel> = new EventEmitter<PropertyAreasUpdateModel>();
  selectedProperty: PropertyUpdateModel = new PropertyUpdateModel();
  selectedPropertyAreas: PropertyAreaModel[] = [];
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('Area'),
      field: 'name',
    },
    {
      header: this.translateService.stream('Description'),
      field: 'description',
      formatter: (rowData: PropertyAreaModel) =>
        rowData.description ? `<a href="${rowData.description}" target="_blank">${this.translateService.instant('Read more')}</a>` : ``,
    },
  ];

  disabledAreas: string[] = [
    '13. APV Landbrug',
    '05. Stalde: Halebid og klargøring',
    '21. DANISH Standard',
    '100. Diverse',
  ];

  constructor(
    public authStateService: AuthStateService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<PropertyAreasEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) model: { selectedProperty: PropertyModel, propertyAreas: PropertyAreaModel[] }
  ) {
    this.selectedProperty = {...model.selectedProperty, languagesIds: []};
    this.selectedPropertyAreas = model.propertyAreas
      .filter(x => (!this.disabledAreas.includes(x.name) || this.authStateService.isAdmin));
  }

  ngOnInit() {
  }

  get getSelectedPropertyAreas() {
    return this.selectedPropertyAreas.filter(x => x.activated);
  }

  getAreaPlanningStatus(area: PropertyAreaModel) {
    return area.status ? 'ON' : 'OFF';
  }

  hide() {
    this.selectedProperty = new PropertyUpdateModel();
    this.dialogRef.close();
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

  updateAreas(propertyAreas: PropertyAreaModel[]) {
    this.selectedPropertyAreas.forEach(x => x.activated = propertyAreas.some(y => y.id === x.id));
  }
}
