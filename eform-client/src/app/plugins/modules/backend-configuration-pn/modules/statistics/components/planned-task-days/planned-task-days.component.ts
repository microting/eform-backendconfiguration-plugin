import {Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges,
  inject
} from '@angular/core';
import {PlannedTaskDaysModel,} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {Store} from '@ngrx/store';
import {selectCurrentUserLocale, selectIsDarkMode} from 'src/app/state';

@AutoUnsubscribe()
@Component({
    selector: 'app-planned-task-days',
    templateUrl: './planned-task-days.component.html',
    styleUrls: ['./planned-task-days.component.scss'],
    standalone: false
})
export class PlannedTaskDaysComponent implements OnChanges, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);

  @Input() plannedTaskDaysModel: PlannedTaskDaysModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<void> = new EventEmitter<void>();
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  labels: string[] = ['Exceeded', 'Today', '1-7 days', '8-30 days', 'Over 30 days'];
  labelsTranslated: string[] = [];
  colorSchemeLight = {
    domain: ['#dc3545', '#a3d7b1', '#a3d7b1', '#a3d7b1', '#a3d7b1']
  };
  customColorsLight = [
    {
      name: this.labels[0],
      value: '#dc3545',
    },
    {
      name: this.labels[1],
      value: '#a3d7b1',
    },
    {
      name: this.labels[2],
      value: '#a3d7b1',
    },
    {
      name: this.labels[3],
      value: '#a3d7b1',
    },
    {
      name: this.labels[4],
      value: '#a3d7b1',
    },
  ];
  colorSchemeDark = {
    domain: ['#dc3545', '#a3d7b1', '#a3d7b1', '#a3d7b1', '#a3d7b1']
  };
  customColorsDark = [
    {
      name: this.labels[0],
      value: '#dc3545',
    },
    {
      name: this.labels[1],
      value: '#a3d7b1',
    },
    {
      name: this.labels[2],
      value: '#a3d7b1',
    },
    {
      name: this.labels[3],
      value: '#a3d7b1',
    },
    {
      name: this.labels[4],
      value: '#a3d7b1',
    },
  ];
  isDarkTheme = true;
  currentDate = format(new Date(), 'P', {locale: this.authStateService.dateFnsLocale});

  isDarkThemeAsyncSub$: Subscription;
  selectCurrentUserLocaleSub$: Subscription;

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
  private selectCurrentUserLocale$ = this.store.select(selectCurrentUserLocale);

  
  constructor() {
    this.isDarkThemeAsyncSub$ = this.selectIsDarkMode$.subscribe((isDarkMode) => {
      this.isDarkTheme = isDarkMode;
    });
  }


  changeData(labelsTranslated: string[], plannedTaskDaysModel: PlannedTaskDaysModel) {
    this.chartData = [
      {
        name: labelsTranslated[0],
        value: plannedTaskDaysModel.exceeded,
      },
      {
        name: labelsTranslated[1],
        value: plannedTaskDaysModel.today,
      },
      {
        name: labelsTranslated[2],
        value: plannedTaskDaysModel.fromFirstToSeventhDays,
      },
      {
        name: labelsTranslated[3],
        value: plannedTaskDaysModel.fromEighthToThirtiethDays,
      },
      {
        name: labelsTranslated[4],
        value: plannedTaskDaysModel.overThirtiethDays,
      },
    ];
    this.customColorsDark = [
      {
        name: labelsTranslated[0],
        value: this.customColorsDark[0].value,
      },
      {
        name: labelsTranslated[1],
        value: this.customColorsDark[1].value,
      },
      {
        name: labelsTranslated[2],
        value: this.customColorsDark[2].value,
      },
      {
        name: labelsTranslated[3],
        value: this.customColorsDark[3].value,
      },
      {
        name: labelsTranslated[4],
        value: this.customColorsDark[4].value,
      },
    ];
    this.customColorsLight = [
      {
        name: labelsTranslated[0],
        value: this.customColorsLight[0].value,
      },
      {
        name: labelsTranslated[1],
        value: this.customColorsLight[1].value,
      },
      {
        name: labelsTranslated[2],
        value: this.customColorsLight[2].value,
      },
      {
        name: labelsTranslated[3],
        value: this.customColorsLight[3].value,
      },
      {
        name: labelsTranslated[4],
        value: this.customColorsLight[4].value,
      },
    ];
  }

  subToGetTranslates() {
    if (!this.selectCurrentUserLocaleSub$) {
      this.selectCurrentUserLocaleSub$ = this.selectCurrentUserLocale$.subscribe(() => {
        const x = this.translateService.instant(this.labels);
        this.labelsTranslated = Object.values(x);
        this.changeData(this.labelsTranslated, this.plannedTaskDaysModel
          || {exceeded: 0, today: 0, fromFirstToSeventhDays: 0, fromEighthToThirtiethDays: 0, overThirtiethDays: 0});
      });
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.plannedTaskDaysModel &&
      !changes.plannedTaskDaysModel.isFirstChange() &&
      changes.plannedTaskDaysModel.currentValue) {
      this.changeData(this.labelsTranslated.length ? this.labelsTranslated : this.labels, this.plannedTaskDaysModel);
      this.subToGetTranslates();
      this.getxAxisTicks();
    }
  }

  ngOnDestroy(): void {
  }
}
