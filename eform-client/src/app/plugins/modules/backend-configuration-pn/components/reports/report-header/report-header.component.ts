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
import {SharedTagModel} from 'src/app/common/models';
import {AuthStateService} from 'src/app/common/store';
import {ReportQuery, ReportStateService} from '../store';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon, PARSING_DATE_FORMAT, WordIcon} from 'src/app/common/const';
import {format, parse} from 'date-fns';

@AutoUnsubscribe()
@Component({
  selector: 'app-items-planning-pn-report-header',
  templateUrl: './report-header.component.html',
  styleUrls: ['./report-header.component.scss'],
})
export class ReportHeaderComponent implements OnInit, OnDestroy {
  @Output()
  generateReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Output()
  downloadReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Output()
  downloadExcelReport: EventEmitter<ReportPnGenerateModel> = new EventEmitter();
  @Input() range: Date[];
  @Input() availableTags: SharedTagModel[] = [];
  generateForm: FormGroup;
  valueChangesSub$: Subscription;

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private formBuilder: FormBuilder,
    private reportStateService: ReportStateService,
    private reportQuery: ReportQuery,
    authStateService: AuthStateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
  ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
    iconRegistry.addSvgIconLiteral('file-excel', sanitizer.bypassSecurityTrustHtml(ExcelIcon));
    dateTimeAdapter.setLocale(authStateService.currentUserLocale);
  }

  ngOnInit() {
    this.generateForm = new FormGroup<any>(
      {
        dateRange: new FormControl(
          this.reportQuery.pageSetting.dateRange
            .map(date => parse(date, PARSING_DATE_FORMAT, new Date())),
          [Validators.required]),
        tagIds: new FormControl(this.reportQuery.pageSetting.filters.tagIds)
      });
    this.valueChangesSub$ = this.generateForm.valueChanges.subscribe(
      (value: { tagIds: number[]; dateRange: Date[] }) => {
        if (value.dateRange.length) {
          const dateFrom = format(value.dateRange[0], `yyyy-MM-dd'T00:00:00.000Z'`);
          const dateTo = format(value.dateRange[1], `yyyy-MM-dd'T00:00:00.000Z'`);
          this.reportStateService.updateDateRange([dateFrom, dateTo]);
        }
      }
    );
    if (!!this.range[0].getDate()) {
      this.generateForm.get('dateRange').setValue(this.range);
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
    this.downloadExcelReport.emit(model);
  }

  private extractData(): ReportPnGenerateModel {
    return new ReportPnGenerateModel({
      dateFrom: this.reportQuery.pageSetting.dateRange[0],
      dateTo: this.reportQuery.pageSetting.dateRange[1],
      tagIds: [...this.reportQuery.pageSetting.filters.tagIds],
    });
  }

  addOrDeleteTagId(tag: SharedTagModel) {
    this.reportStateService.addOrRemoveTagIds(tag.id);
  }

  ngOnDestroy(): void {
  }
}
