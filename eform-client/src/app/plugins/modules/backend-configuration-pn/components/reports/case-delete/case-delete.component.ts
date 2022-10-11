import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { BackendConfigurationPnReportService } from '../../../services';
import { ReportEformItemModel } from '../../../models';

@Component({
  selector: 'app-case-delete',
  templateUrl: './case-delete.component.html',
  styleUrls: ['./case-delete.component.scss'],
})
export class CaseDeleteComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() planningCaseDeleted: EventEmitter<void> = new EventEmitter<void>();
  reportEformItemModel: ReportEformItemModel = new ReportEformItemModel();
  constructor(
    private backendConfigurationPnReportService: BackendConfigurationPnReportService
  ) {}

  ngOnInit() {}

  show(reportEformItemModel: ReportEformItemModel) {
    this.reportEformItemModel = reportEformItemModel;
    this.frame.show();
  }

  deletePlanning() {
    this.backendConfigurationPnReportService
      .deleteCase(this.reportEformItemModel.id)
      .subscribe((data) => {
        if (data && data.success) {
          this.planningCaseDeleted.emit();
          this.frame.hide();
        }
      });
  }
}
