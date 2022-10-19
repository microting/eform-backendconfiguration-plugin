import {Component, OnInit, ViewChild} from '@angular/core';

@Component({
  selector: 'app-backend-configuration-case-header',
  templateUrl: './backend-configuration-case-header.component.html',
  styleUrls: ['./backend-configuration-case-header.component.scss']
})
export class BackendConfigurationCaseHeaderComponent implements OnInit {
  // @Input() contractInspectionModel: ContractInspectionModel = new ContractInspectionModel();
  // @Input() customerModel: RentableItemCustomerModel = new RentableItemCustomerModel();
  @ViewChild('reportCropperModal', {static: false}) reportCropperModal;
  constructor() { }

  ngOnInit() {
  }
}
