import {Component, OnInit, ViewChild} from '@angular/core';
import {TranslatePipe} from '@ngx-translate/core';
import {MatCard, MatCardContent, MatCardTitle} from '@angular/material/card';

@Component({
    selector: 'app-installation-case-header',
    imports: [TranslatePipe, MatCard, MatCardContent, MatCardTitle],
    templateUrl: './compliance-case-header.component.html',
    styleUrls: ['./compliance-case-header.component.scss'],
})
export class ComplianceCaseHeaderComponent implements OnInit {
  // @Input() contractInspectionModel: ContractInspectionModel = new ContractInspectionModel();
  // @Input() customerModel: RentableItemCustomerModel = new RentableItemCustomerModel();
  @ViewChild('reportCropperModal', {static: false}) reportCropperModal;
  constructor() { }

  ngOnInit() {
  }
}
