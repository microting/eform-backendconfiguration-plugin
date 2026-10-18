import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  OnDestroy,
  inject
} from '@angular/core';
import {FormControl, FormGroup, Validators, ReactiveFormsModule} from '@angular/forms';
import {ReportPnGenerateModel} from '../../../models/report';
import {SharedTagModel} from 'src/app/common/models';
import {ReportStateService} from '../store';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {MatIconRegistry, MatIcon} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, PARSING_DATE_FORMAT, WordIcon, PdfIcon} from 'src/app/common/const';
import {format, parse} from 'date-fns';
import {TranslatePipe} from '@ngx-translate/core';
import {MatFormField, MatLabel, MatPrefix} from '@angular/material/form-field';
import {MtxSelect} from '@ng-matero/extensions/select';
import {MatTooltip} from '@angular/material/tooltip';
import {MatDatepickerToggle, MatDateRangeInput, MatDateRangePicker, MatStartDate, MatEndDate} from '@angular/material/datepicker';

@AutoUnsubscribe()
@Component({
    selector: 'app-backend-pn-report-header',
    imports: [ReactiveFormsModule, TranslatePipe, MatIcon, MatFormField, MatLabel, MatPrefix, MtxSelect, MatTooltip, MatDatepickerToggle, MatDateRangeInput, MatDateRangePicker, MatStartDate, MatEndDate],
    templateUrl: './report-header.component.html',
    styleUrls: ['./report-header.component.scss'],
})
export class ReportHeaderComponent implements OnInit, OnDestroy {
  private reportStateService = inject(ReportStateService);

  @Output()
  generateReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Output()
  downloadReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Input() availableTags: SharedTagModel[] = [];
  generateForm: FormGroup<{
    tagIds: FormControl<number[]>,
    dateRange: FormGroup<{ dateFrom: FormControl<Date>, dateTo: FormControl<Date> }>
  }>;

  valueChangesSub$: Subscription;



  ngOnInit() {

    const reportPnGenerateModel = this.reportStateService.extractData();
    this.generateForm = new FormGroup(
      {
        tagIds: new FormControl(reportPnGenerateModel.tagIds),
        dateRange: new FormGroup({
          dateFrom: new FormControl(
            reportPnGenerateModel.dateFrom &&
            parse(reportPnGenerateModel.dateFrom, PARSING_DATE_FORMAT, new Date()), [Validators.required]),
          dateTo: new FormControl(
            reportPnGenerateModel.dateTo &&
            parse(reportPnGenerateModel.dateTo, PARSING_DATE_FORMAT, new Date()), [Validators.required]),
  },),
      });
    this.valueChangesSub$ = this.generateForm.valueChanges.subscribe(
      (value) => {
        if (value.dateRange.dateFrom && value.dateRange.dateTo) {
          const dateFrom = format(value.dateRange.dateFrom, PARSING_DATE_FORMAT);
          const dateTo = format(value.dateRange.dateTo, PARSING_DATE_FORMAT);
          this.reportStateService.updateDateRange({startDate: dateFrom, endDate: dateTo});
        }
      }
    );
  }

  onSubmit() {
    const model = this.reportStateService.extractData();
    this.generateReport.emit(model);
  }

  onWordSave() {
    const model = this.reportStateService.extractData();
    model.type = 'docx';
    this.downloadReport.emit(model);
  }

  onExcelSave() {
    const model = this.reportStateService.extractData();
    model.type = 'xlsx';
    this.downloadReport.emit(model);
  }

  onPdfSave() {
    const model = this.reportStateService.extractData();
    model.type = 'pdf';
    this.downloadReport.emit(model);
  }

  addOrDeleteTagId(tag: SharedTagModel) {
    this.reportStateService.addOrRemoveTagIds(tag.id);
  }

  ngOnDestroy(): void {
  }
}
