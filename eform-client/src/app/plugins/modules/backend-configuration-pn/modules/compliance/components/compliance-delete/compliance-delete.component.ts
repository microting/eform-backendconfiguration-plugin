import {Component, OnInit,
  inject
} from '@angular/core';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogActions} from '@angular/material/dialog';
import {ComplianceModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {BackendConfigurationPnCompliancesService} from 'src/app/plugins/modules/backend-configuration-pn/services';

@Component({
    selector: 'app-compliance-delete',
    imports: [TranslatePipe, MatDialogTitle, MatDialogActions],
    templateUrl: './compliance-delete.component.html',
    styleUrls: ['./compliance-delete.component.scss'],
})
export class ComplianceDeleteComponent implements OnInit {
  private service = inject(BackendConfigurationPnCompliancesService);
  private translateService = inject(TranslateService);
  public dialogRef = inject(MatDialogRef<ComplianceDeleteComponent>);
  public complianceModel = inject<ComplianceModel>(MAT_DIALOG_DATA);

  
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
