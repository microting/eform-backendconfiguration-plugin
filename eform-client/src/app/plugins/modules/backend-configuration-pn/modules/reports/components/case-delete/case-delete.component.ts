import {
  Component,
  OnInit,
  inject
} from '@angular/core';
import {BackendConfigurationPnReportService} from '../../../../services';
import {ReportEformItemModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';
import {DateFormatterComponent} from 'src/app/common/modules/eform-shared/components';

@Component({
    selector: 'app-case-delete',
    imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions, DateFormatterComponent],
    templateUrl: './case-delete.component.html',
    styleUrls: ['./case-delete.component.scss'],
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
