import {Component, OnInit, ViewChild} from '@angular/core';
import {
  ComplianceCaseModule
} from 'src/app/plugins/modules/backend-configuration-pn/modules/compliance/components/compliance-case/compliance-case.module';

@Component({
  selector: 'app-installation-case-header',
  templateUrl: './compliance-case-header.component.html',
  styleUrls: ['./compliance-case-header.component.scss']
})
export class ComplianceCaseHeaderComponent implements OnInit {
  // @Input() contractInspectionModel: ContractInspectionModel = new ContractInspectionModel();
  // @Input() customerModel: RentableItemCustomerModel = new RentableItemCustomerModel();
  @ViewChild('reportCropperModal', {static: false}) reportCropperModal;
  constructor() { }

  ngOnInit() {
  }
}
