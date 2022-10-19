import {
  Component, OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import {PropertyAreaModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService, BackendConfigurationPnReportService} from '../../../../services';
import * as R from 'ramda';
import {saveAs} from 'file-saver';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {ToastrService} from 'ngx-toastr';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-docx-report-modal',
  templateUrl: './property-docx-report-modal.component.html',
  styleUrls: ['./property-docx-report-modal.component.scss'],
})
export class PropertyDocxReportModalComponent implements OnInit, OnDestroy {
  @ViewChild('frame', { static: false }) frame;
  propertyId: number;
  selectedArea: PropertyAreaModel;
  selectedYear: number;
  areasList: PropertyAreaModel[] = [];
  years: number[] = [];

  downloadReportSub$: Subscription;
  getPropertyAreasSub$: Subscription;

  constructor(
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private reportService: BackendConfigurationPnReportService,
    private toasterService: ToastrService,
  ) {}

  ngOnInit() {
    const currentYear = new Date().getFullYear();
    this.years = R.range(currentYear - 1, currentYear + 10);
  }

  show(propertyId: number) {
    this.propertyId = propertyId;
    this.getPropertyAreasSub$ = this.backendConfigurationPnPropertiesService
      .getPropertyAreas(propertyId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.areasList = data.model.filter(x => x.activated && x.name === '24. IE-indberetning');
          this.frame.show();
        }
      });
  }

  hide() {
    this.frame.hide();
    this.propertyId = undefined;
    this.selectedArea = undefined;
    this.selectedYear = undefined;
  }

  onDownloadReport() {
    this.downloadReportSub$ = this.reportService
      .downloadReport(this.propertyId, this.selectedArea.areaId, this.selectedYear)
      .subscribe(
        (data) => {
          saveAs(data, this.selectedArea.name + '_' + this.selectedYear + '_report.docx');
        },
        (data) => {
          this.toasterService.error('Error downloading report');
        }
      );
  }

  get isDisabledDownloadButton(): boolean {
    return !this.propertyId || !this.selectedArea || !this.selectedYear;
  }

  ngOnDestroy(): void {
  }
}
