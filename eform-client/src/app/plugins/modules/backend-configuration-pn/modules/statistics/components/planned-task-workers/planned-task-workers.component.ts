import {Component, Input, OnChanges, OnDestroy, SimpleChanges} from '@angular/core';
import {
  PlannedTaskWorkers,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';

@AutoUnsubscribe()
@Component({
  selector: 'app-planned-task-workers',
  templateUrl: './planned-task-workers.component.html',
  styleUrls: ['./planned-task-workers.component.scss'],
})
export class PlannedTaskWorkersComponent implements OnChanges, OnDestroy {
  @Input() plannedTaskWorkers: PlannedTaskWorkers;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  chartData: { name: string, value: number }[] = [];
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

  constructor(
    private translateService: TranslateService,
    private authStateService: AuthStateService
  ) {
    this.isDarkThemeAsyncSub$ = authStateService.isDarkThemeAsync
      .subscribe(isDarkTheme => this.isDarkTheme = isDarkTheme);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.plannedTaskWorkers &&
      !changes.plannedTaskWorkers.isFirstChange() &&
      changes.plannedTaskWorkers.currentValue) {
      this.chartData = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: x.statValue}));
      this.customColorsDark = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.customColorsLight = this.plannedTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
    }
  }

  ngOnDestroy(): void {
  }
}
