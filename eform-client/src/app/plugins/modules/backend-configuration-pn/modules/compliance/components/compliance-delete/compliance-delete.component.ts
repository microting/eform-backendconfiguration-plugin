import {Component, Inject, OnInit} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ComplianceModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {BackendConfigurationPnCompliancesService} from 'src/app/plugins/modules/backend-configuration-pn/services';

@Component({
  selector: 'app-compliance-delete',
  templateUrl: './compliance-delete.component.html',
  styleUrls: ['./compliance-delete.component.scss'],
})
export class ComplianceDeleteComponent implements OnInit {
  constructor(
    private service: BackendConfigurationPnCompliancesService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<ComplianceDeleteComponent>,
    @Inject(MAT_DIALOG_DATA) public complianceModel: ComplianceModel
  ) { }
  ngOnInit() {
  }
  deleteCompliance() {
    this.service.deleteCompliance(this.complianceModel.id).subscribe((data) => {
      if (data && data.success) {
        this.hide(true);
      }
    });
    // this.hide(true);
  }

  hide(result = false) {
    this.dialogRef.close(result);
  }
}
