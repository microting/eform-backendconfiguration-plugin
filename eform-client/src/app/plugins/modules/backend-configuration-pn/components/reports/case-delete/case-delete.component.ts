import {
  Component,
  Inject,
  OnInit,
} from '@angular/core';
import {BackendConfigurationPnReportService} from '../../../services';
import {ReportEformItemModel} from '../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-case-delete',
    templateUrl: './case-delete.component.html',
    styleUrls: ['./case-delete.component.scss'],
    standalone: false
})
export class CaseDeleteComponent implements OnInit {
  constructor(
    private backendConfigurationPnReportService: BackendConfigurationPnReportService,
    public dialogRef: MatDialogRef<CaseDeleteComponent>,
    @Inject(MAT_DIALOG_DATA) public reportEformItemModel: ReportEformItemModel
  ) {
  }

  ngOnInit() {
  }

  deletePlanning() {
    this.backendConfigurationPnReportService
      .deleteCase(this.reportEformItemModel.id)
      .subscribe((data) => {
        if (data && data.success) {
          this.hide(true);
        }
      });
  }

  hide(result = false) {
    this.dialogRef.close(result);
  }
}
