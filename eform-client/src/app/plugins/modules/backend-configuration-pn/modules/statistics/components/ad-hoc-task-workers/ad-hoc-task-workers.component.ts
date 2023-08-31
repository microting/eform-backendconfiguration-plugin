import {Component, Input, OnChanges, OnDestroy, SimpleChanges} from '@angular/core';
import {
  AdHocTaskWorkers,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';

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
    if (changes.adHocTaskWorkers &&
      !changes.adHocTaskWorkers.isFirstChange() &&
      changes.adHocTaskWorkers.currentValue) {
      this.chartData = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: x.statValue}));
      this.customColorsDark = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
      this.customColorsLight = this.adHocTaskWorkers.taskWorkers.map(x => ({name: x.workerName, value: '#0000ff'}));
    }
  }

  ngOnDestroy(): void {
  }
}
