import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {AdHocTaskWorkers,} from '../../../../models';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {selectIsDarkMode} from 'src/app/state';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-ad-hoc-task-workers',
    templateUrl: './ad-hoc-task-workers.component.html',
    styleUrls: ['./ad-hoc-task-workers.component.scss'],
    standalone: false
})
export class AdHocTaskWorkersComponent implements OnChanges, OnDestroy {
  @Input() adHocTaskWorkers: AdHocTaskWorkers;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<number | null> = new EventEmitter<number | null>();
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  colorSchemeLight = {
    domain: ['#a3d7b1']
  };
  customColorsLight = [];
  colorSchemeDark = {
    domain: ['#a3d7b1']
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
    if (this.chartData.length > 0) {
      const max = Math.max.apply(Math, this.chartData.map(o => o.value)) + 1;
      if (max < 11) {
        this.xAxisTicks = Array.from(Array(max).keys());
      } else if (max >= 11 && max <= 20) {
        this.xAxisTicks = Array.from(Array(max + 1).keys()).filter(x => x % 5 === 0);
      } else if (max >= 21 && max <= 100) {
        this.xAxisTicks = Array.from(Array(max + 1).keys()).filter(x => x % 10 === 0);
      } else {
        this.xAxisTicks = Array.from(Array(max + 1).keys()).filter(x => x % 20 === 0);
      }
    } else {
      this.xAxisTicks = [0];
    }
  }

  private selectIsDarkMode$ = this.store.select(selectIsDarkMode);

  constructor(
    private store: Store,
    private authStateService: AuthStateService
  ) {
    this.isDarkThemeAsyncSub$ = this.selectIsDarkMode$.subscribe((isDarkMode) => {
      this.isDarkTheme = isDarkMode;
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.adHocTaskWorkers &&
      !changes.adHocTaskWorkers.isFirstChange() &&
      changes.adHocTaskWorkers.currentValue) {
      this.chartData = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: x.statValue}));
      this.customColorsDark = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#a3d7b1'}));
      this.customColorsLight = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#a3d7b1'}));
      this.getxAxisTicks();
    }
  }

  onClickOnDiagram(chartData: { name: string, value: number } = null) {
    if (!chartData) {
      this.clickOnDiagram.emit();
    } else {
      const workerId = this.adHocTaskWorkers.taskWorkers.find(x => x.workerName === chartData.name).workerId;
      this.clickOnDiagram.emit(workerId);
    }
  }

  ngOnDestroy(): void {
  }
}
