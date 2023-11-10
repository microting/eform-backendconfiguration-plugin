import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {
  AdHocTaskPrioritiesModel,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {selectIsDarkMode} from "src/app/state/auth/auth.selector";
import {Store} from "@ngrx/store";

@AutoUnsubscribe()
@Component({
  selector: 'app-ad-hoc-task-priorities',
  templateUrl: './ad-hoc-task-priorities.component.html',
  styleUrls: ['./ad-hoc-task-priorities.component.scss'],
})
export class AdHocTaskPrioritiesComponent implements OnChanges, OnDestroy {
  @Input() adHocTaskPrioritiesModel: AdHocTaskPrioritiesModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<void> = new EventEmitter<void>();
  currentDate = format(new Date(), 'P', {locale: this.authStateService.dateFnsLocale});
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  colorSchemeLight = {
    domain: ['#ff0000', '#ffbb33', '#0000ff', '#1414fa']
  };
  customColorsLight = [
    {
      name: this.translateService.instant('Urgent'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('High'),
      value: '#ffbb33',
    },
    {
      name: this.translateService.instant('Middle'),
      value: '#0000ff',
    },
    {
      name: this.translateService.instant('Low'),
      value: '#1414fa',
    },
  ];
  colorSchemeDark = {
    domain: ['#ff0000', '#ffbb33', '#0000ff', '#1414fa']
  };
  customColorsDark = [
    {
      name: this.translateService.instant('Urgent'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('High'),
      value: '#ffbb33',
    },
    {
      name: this.translateService.instant('Middle'),
      value: '#0000ff',
    },
    {
      name: this.translateService.instant('Low'),
      value: '#1414fa',
    },
  ];
  isDarkTheme = true;

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
    if (changes.adHocTaskPrioritiesModel &&
      !changes.adHocTaskPrioritiesModel.isFirstChange() &&
      changes.adHocTaskPrioritiesModel.currentValue) {
      this.chartData = [
        {
          name: this.translateService.instant('Urgent'),
          value: this.adHocTaskPrioritiesModel.urgent,
        },
        {
          name: this.translateService.instant('High'),
          value: this.adHocTaskPrioritiesModel.high,
        },
        {
          name: this.translateService.instant('Middle'),
          value: this.adHocTaskPrioritiesModel.middle,
        },
        {
          name: this.translateService.instant('Low'),
          value: this.adHocTaskPrioritiesModel.low,
        },
      ];
      this.getxAxisTicks();
    }
  }

  ngOnDestroy(): void {
  }
}
