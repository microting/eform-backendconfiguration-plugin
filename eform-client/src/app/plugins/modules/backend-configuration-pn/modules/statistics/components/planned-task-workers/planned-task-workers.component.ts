import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {PlannedTaskWorkers,} from '../../../../models';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Store} from '@ngrx/store';
import {selectIsDarkMode} from 'src/app/state';

@AutoUnsubscribe()
@Component({
    selector: 'app-planned-task-workers',
    templateUrl: './planned-task-workers.component.html',
    styleUrls: ['./planned-task-workers.component.scss'],
    standalone: false
})
export class PlannedTaskWorkersComponent implements OnChanges, OnDestroy {
  @Input() plannedTaskWorkers: PlannedTaskWorkers;
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
    if (changes.plannedTaskWorkers &&
      !changes.plannedTaskWorkers.isFirstChange() &&
      changes.plannedTaskWorkers.currentValue) {
      this.chartData = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: x.statValue}));
      this.customColorsDark = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.customColorsLight = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.getxAxisTicks();
    }
  }

  ngOnDestroy(): void {
  }

  onClickOnDiagram(chartData: { name: string, value: number } = null) {
    if (!chartData) {
      this.clickOnDiagram.emit();
    } else {
      const workerId = this.plannedTaskWorkers.taskWorkers.find(x => x.workerName === chartData.name).workerId;
      this.clickOnDiagram.emit(workerId);
    }
  }
}
