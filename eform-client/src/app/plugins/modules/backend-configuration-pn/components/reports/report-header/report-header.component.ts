import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  OnDestroy,
} from '@angular/core';
import {FormBuilder, FormControl, FormGroup, Validators} from '@angular/forms';
import {ReportPnGenerateModel} from '../../../models/report';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {FiltrationStateModel, SharedTagModel} from 'src/app/common/models';
import {AuthStateService} from 'src/app/common/store';
import {ReportStateService} from '../store';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, PARSING_DATE_FORMAT, WordIcon, PdfIcon} from 'src/app/common/const';
import {format, parse} from 'date-fns';
import {selectCurrentUserLocale} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {
  selectReportsV1Filters,
  selectReportsV1DateRange
} from 'src/app/plugins/modules/backend-configuration-pn/state/reports-v1/reports-v1.selector';

@AutoUnsubscribe()
@Component({
  selector: 'app-backend-pn-report-header',
  templateUrl: './report-header.component.html',
  styleUrls: ['./report-header.component.scss'],
})
export class ReportHeaderComponent implements OnInit, OnDestroy {
  @Output()
  generateReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Output()
  downloadReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Input() range: Date[];
  @Input() availableTags: SharedTagModel[] = [];
  generateForm: FormGroup;
  valueChangesSub$: Subscription;
  private selectCurrentUserLocale$ = this.authStore.select(selectCurrentUserLocale);
  private selectReportsV1Filters$ = this.authStore.select(selectReportsV1Filters);
  private selectReportsV1DateRange$ = this.authStore.select(selectReportsV1DateRange);

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private formBuilder: FormBuilder,
    private authStore: Store,
    private reportStateService: ReportStateService,
    authStateService: AuthStateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
  ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
    iconRegistry.addSvgIconLiteral('file-excel', sanitizer.bypassSecurityTrustHtml(ExcelIcon));
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
    this.selectCurrentUserLocale$.subscribe((locale) => {
      dateTimeAdapter.setLocale(locale);
    });
  }

  ngOnInit() {
    let _filters: FiltrationStateModel;
    this.selectReportsV1Filters$.subscribe((filters) => {
      _filters = filters;
    }).unsubscribe();
    let dateRange: { startDate: string, endDate: string };
    this.selectReportsV1DateRange$.subscribe((range) => {
      dateRange = range;
    }).unsubscribe();
    this.generateForm = new FormGroup(
      {
        tagIds: new FormControl(_filters.tagIds),
        dateRange: new FormGroup({
          dateFrom: new FormControl(
            dateRange.startDate &&
            parse(dateRange.startDate, PARSING_DATE_FORMAT, new Date()), [Validators.required]),
          dateTo: new FormControl(
            dateRange.endDate &&
            parse(dateRange.endDate, PARSING_DATE_FORMAT, new Date()), [Validators.required]),
        },),
      });
    this.valueChangesSub$ = this.generateForm.valueChanges.subscribe(
      (value: { tagIds: number[]; dateRange: {dateFrom: Date, dateTo: Date} }) => {
        if(value.dateRange.dateFrom) {
          const dateFrom = format(value.dateRange.dateFrom, PARSING_DATE_FORMAT);
          this.reportStateService.updateDateRange({startDate: dateFrom});
        }
        if(value.dateRange.dateTo) {
          const dateTo = format(value.dateRange.dateTo, PARSING_DATE_FORMAT);
          this.reportStateService.updateDateRange({endDate: dateTo});
        }
      }
    );
    if (!!this.range[0].getDate()) {
      this.generateForm.get('dateRange.dateFrom').setValue(this.range[0]);
      this.generateForm.get('dateRange.dateTo').setValue(this.range[1]);
    }
  }

  onSubmit() {
    const model = this.extractData();
    this.generateReport.emit(model);
  }

  onWordSave() {
    const model = this.extractData();
    model.type = 'docx';
    this.downloadReport.emit(model);
  }

  onExcelSave() {
    const model = this.extractData();
    model.type = 'xlsx';
    this.downloadReport.emit(model);
  }

  private extractData(): ReportPnGenerateModel {
    let _filters: FiltrationStateModel;
    this.selectReportsV1Filters$.subscribe((filters) => {
      _filters = filters;
    }).unsubscribe();
    let dateRange: { startDate: string, endDate: string };
    this.selectReportsV1DateRange$.subscribe((range) => {
      dateRange = range;
    }).unsubscribe();
    return new ReportPnGenerateModel({
      dateFrom: dateRange.startDate,
      dateTo: dateRange.endDate,
      tagIds: [..._filters.tagIds],
    });
  }

  addOrDeleteTagId(tag: SharedTagModel) {
    this.reportStateService.addOrRemoveTagIds(tag.id);
  }

  ngOnDestroy(): void {
  }

  onPdfSave() {
    const model = this.extractData();
    model.type = 'pdf';
    this.downloadReport.emit(model);
  }
}
