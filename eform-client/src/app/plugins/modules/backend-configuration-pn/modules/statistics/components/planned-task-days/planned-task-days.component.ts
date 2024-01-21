import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {
  PlannedTaskDaysModel,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {Store} from '@ngrx/store';
import {selectIsDarkMode} from 'src/app/state/auth/auth.selector';

@AutoUnsubscribe()
@Component({
  selector: 'app-planned-task-days',
  templateUrl: './planned-task-days.component.html',
  styleUrls: ['./planned-task-days.component.scss'],
})
export class PlannedTaskDaysComponent implements OnChanges, OnDestroy {
  @Input() plannedTaskDaysModel: PlannedTaskDaysModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<void> = new EventEmitter<void>();
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  colorSchemeLight = {
    domain: ['#ff0000', '#ffbb33', '#0000ff', '#1414fa', '#3b3bff']
  };
  customColorsLight = [
    {
      name: this.translateService.instant('Exceeded'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('Today'),
      value: '#ffbb33',
    },
    {
      name: this.translateService.instant('1-7 days'),
      value: '#0000ff',
    },
    {
      name: this.translateService.instant('8-30 days'),
      value: '#1414fa',
    },
    {
      name: this.translateService.instant('Over 30 days'),
      value: '#3b3bff',
    },
  ];
  colorSchemeDark = {
    domain: ['#ff0000', '#ffbb33', '#0000ff', '#1414fa', '#3b3bff']
  };
  customColorsDark = [
    {
      name: this.translateService.instant('Exceeded'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('Today'),
      value: '#ffbb33',
    },
    {
      name: this.translateService.instant('1-7 days'),
      value: '#0000ff',
    },
    {
      name: this.translateService.instant('8-30 days'),
      value: '#1414fa',
    },
    {
      name: this.translateService.instant('Over 30 days'),
      value: '#3b3bff',
    },
  ];
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
    if (changes.plannedTaskDaysModel &&
      !changes.plannedTaskDaysModel.isFirstChange() &&
      changes.plannedTaskDaysModel.currentValue) {
      this.chartData = [
        {
          name: this.translateService.instant('Exceeded'),
          value: this.plannedTaskDaysModel.exceeded,
        },
        {
          name: this.translateService.instant('Today'),
          value: this.plannedTaskDaysModel.today,
        },
        {
          name: this.translateService.instant('1-7 days'),
          value: this.plannedTaskDaysModel.fromFirstToSeventhDays,
        },
        {
          name: this.translateService.instant('8-30 days'),
          value: this.plannedTaskDaysModel.fromEighthToThirtiethDays,
        },
        {
          name: this.translateService.instant('Over 30 days'),
          value: this.plannedTaskDaysModel.overThirtiethDays,
        },
      ];
      this.getxAxisTicks();
    }
  }

  ngOnDestroy(): void {
  }
}
