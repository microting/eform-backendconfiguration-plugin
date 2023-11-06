import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {
  AdHocTaskWorkers,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {selectIsDarkMode} from "src/app/state/auth/auth.selector";
import {Store} from "@ngrx/store";

@AutoUnsubscribe()
@Component({
  selector: 'app-ad-hoc-task-workers',
  templateUrl: './ad-hoc-task-workers.component.html',
  styleUrls: ['./ad-hoc-task-workers.component.scss'],
})
export class AdHocTaskWorkersComponent implements OnChanges, OnDestroy {
  @Input() adHocTaskWorkers: AdHocTaskWorkers;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<number | null> = new EventEmitter<number | null>();
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  colorSchemeLight = {
    domain: ['#0000ff']
  };
  customColorsLight = [];
  colorSchemeDark = {
    domain: ['#0000ff']
  };
  customColorsDark = [];
  isDarkTheme = true;
  currentDate = format(new Date(), 'P', {locale: this.authStateService.dateFnsLocale});

  isDarkThemeAsyncSub$: Subscription;

  get customColors(): { name: any, value: string }[] {
    if (this.isDarkTheme) {
      return this.customColorsDark;
    } else {
      return this.customColorsLight;
    }
  }

  get colorScheme(): { domain: string[] } {
    if (this.isDarkTheme) {
      return this.colorSchemeDark;
    } else {
      return this.colorSchemeLight;
    }
  }

  axisFormat(val) {
    if (val % 1 === 0) {
      return val.toLocaleString();
    } else {
      return '';
    }
  }

  getxAxisTicks() {
    // loop through the data and find the biggest value, then create an array from 0 to that value
    if (this.chartData.length > 0) {
      const max = Math.max.apply(Math, this.chartData.map(o => o.value)) + 1;
      this.xAxisTicks = Array.from(Array(max).keys());
    }
  }
  private selectIsDarkMode$ = this.store.select(selectIsDarkMode);

  constructor(
    private store: Store,
    private translateService: TranslateService,
    private authStateService: AuthStateService
  ) {
    this.selectIsDarkMode$.subscribe((isDarkMode) => {
      this.isDarkTheme = isDarkMode;
    });
    // this.isDarkThemeAsyncSub$ = authStateService.isDarkThemeAsync
    //   .subscribe(isDarkTheme => this.isDarkTheme = isDarkTheme);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.adHocTaskWorkers &&
      !changes.adHocTaskWorkers.isFirstChange() &&
      changes.adHocTaskWorkers.currentValue) {
      this.chartData = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: x.statValue}));
      this.customColorsDark = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.customColorsLight = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.getxAxisTicks();
    }
  }

  onClickOnDiagram(chartData: { name: string, value: number } = null) {
    if(!chartData){
      this.clickOnDiagram.emit();
    } else {
      const workerId = this.adHocTaskWorkers.taskWorkers.find(x => x.workerName === chartData.name).workerId;
      this.clickOnDiagram.emit(workerId);
    }
  }

  ngOnDestroy(): void {
  }
}
