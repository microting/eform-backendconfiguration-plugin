import {
  Component,
  OnInit,
  inject
} from '@angular/core';
import {BackendConfigurationPnReportService} from '../../../../services';
import {ReportEformItemModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-case-delete',
    templateUrl: './case-delete.component.html',
    styleUrls: ['./case-delete.component.scss'],
    standalone: false
})
export class CaseDeleteComponent implements OnInit {
  private backendConfigurationPnReportService = inject(BackendConfigurationPnReportService);
  public dialogRef = inject(MatDialogRef<CaseDeleteComponent>);
  public reportEformItemModel = inject<ReportEformItemModel>(MAT_DIALOG_DATA);

  

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
