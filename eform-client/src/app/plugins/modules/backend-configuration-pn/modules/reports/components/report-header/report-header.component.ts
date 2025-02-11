import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  OnDestroy,
} from '@angular/core';
import {FormControl, FormGroup, Validators} from '@angular/forms';
import {ReportPnGenerateModel} from '../../../../models';
import {SharedTagModel} from 'src/app/common/models';
import {ReportStateService} from '../store';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, PARSING_DATE_FORMAT, WordIcon, PdfIcon} from 'src/app/common/const';
import {format, parse} from 'date-fns';

@AutoUnsubscribe()
@Component({
    selector: 'app-backend-pn-report-header',
    templateUrl: './report-header.component.html',
    styleUrls: ['./report-header.component.scss'],
    standalone: false
})
// REPORTS V2
export class ReportHeaderComponent implements OnInit, OnDestroy {
  @Output()
  generateReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Output()
  downloadReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Input() availableTags: SharedTagModel[] = [];
  generateForm: FormGroup;
  valueChangesSub$: Subscription;

  constructor(
    private reportStateService: ReportStateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
  ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
    iconRegistry.addSvgIconLiteral('file-excel', sanitizer.bypassSecurityTrustHtml(ExcelIcon));
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
  }

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
    this.generateReport.emit({...model, version2: true});
  }

  onWordSave() {
    const model = this.reportStateService.extractData();
    this.downloadReport.emit({...model, type: 'docx',version2: true});
  }

  onExcelSave() {
    const model = this.reportStateService.extractData();
    this.downloadReport.emit({...model, type: 'xlsx',version2: true});
  }

  onPdfSave() {
    const model = this.reportStateService.extractData();
    this.downloadReport.emit({...model, type: 'pdf',version2: true});
  }

  addOrDeleteTagId(tag: SharedTagModel) {
    this.reportStateService.addOrRemoveTagIds(tag.id);
  }

  ngOnDestroy(): void {
  }
}
