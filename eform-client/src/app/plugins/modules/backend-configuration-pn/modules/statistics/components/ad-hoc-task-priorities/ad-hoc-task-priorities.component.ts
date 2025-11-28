import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject} from '@angular/core';
import {AdHocTaskPrioritiesModel,} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {selectCurrentUserLocale, selectIsDarkMode} from 'src/app/state';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-ad-hoc-task-priorities',
    templateUrl: './ad-hoc-task-priorities.component.html',
    styleUrls: ['./ad-hoc-task-priorities.component.scss'],
    standalone: false
})
export class AdHocTaskPrioritiesComponent implements OnChanges, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);

  @Input() adHocTaskPrioritiesModel: AdHocTaskPrioritiesModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<number | null> = new EventEmitter<number | null>();
  currentDate = format(new Date(), 'P', {locale: this.authStateService.dateFnsLocale});
  chartData: { name: string, value: number }[] = [];
  priorityNames: string[] = ['Urgent', 'High', 'Middle', 'Low'];
  priorityNamesTranslated: string[] = [];
  xAxisTicks: any[] = [];
  colorSchemeLight = {
    domain: ['#a71d2a', '#dc3545', '#0000ff', '#f8d7da']
  };
  customColorsLight = [
    {
      name: this.priorityNames[0],
      value: '#a71d2a',
    },
    {
      name: this.priorityNames[1],
      value: '#dc3545',
    },
    {
      name: this.priorityNames[2],
      value: '#f5a5a8',
    },
    {
      name: this.priorityNames[3],
      value: '#f8d7da',
    },
  ];
  colorSchemeDark = {
    domain: ['#a71d2a', '#dc3545', '#f5a5a8', '#f8d7da']
  };
  customColorsDark = [
    {
      name: this.priorityNames[0],
      value: '#a71d2a',
    },
    {
      name: this.priorityNames[1],
      value: '#dc3545',
    },
    {
      name: this.priorityNames[2],
      value: '#f5a5a8',
    },
    {
      name: this.priorityNames[3],
      value: '#f8d7da',
    },
  ];
  isDarkTheme = true;

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


  changeData(labelsTranslated: string[], adHocTaskPrioritiesModel: AdHocTaskPrioritiesModel) {
    this.chartData = [
      {
        name: labelsTranslated[0],
        value: adHocTaskPrioritiesModel.urgent,
      },
      {
        name: labelsTranslated[1],
        value: adHocTaskPrioritiesModel.high,
      },
      {
        name: labelsTranslated[2],
        value: adHocTaskPrioritiesModel.middle,
      },
      {
        name: labelsTranslated[3],
        value: adHocTaskPrioritiesModel.low,
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
    ];
  }

  subToGetTranslates() {
    if (!this.selectCurrentUserLocaleSub$) {
      this.selectCurrentUserLocaleSub$ = this.selectCurrentUserLocale$.subscribe(() => {
        const x = this.translateService.instant(this.priorityNames);
        this.priorityNamesTranslated = Object.values(x);
        this.changeData(this.priorityNamesTranslated, this.adHocTaskPrioritiesModel || {urgent: 0, high: 0, middle: 0, low: 0});
      });
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.adHocTaskPrioritiesModel &&
      !changes.adHocTaskPrioritiesModel.isFirstChange() &&
      changes.adHocTaskPrioritiesModel.currentValue) {
      this.changeData(this.priorityNamesTranslated.length ?
        this.priorityNamesTranslated : this.priorityNames, this.adHocTaskPrioritiesModel);
      this.subToGetTranslates();
      this.getxAxisTicks();
    }
  }

  onClickOnDiagram(chartData: { name: string, value: number } = null) {
    if (!chartData) {
      this.clickOnDiagram.emit();
    } else {
      // lookup the chartData.name in the priorityNames array and get the index
      const translatedPriorityIndex = this.priorityNamesTranslated.indexOf(chartData.name);
      //const priorityIndex = this.priorityNames.indexOf(chartData.name);
      this.clickOnDiagram.emit(translatedPriorityIndex + 1);
    }
  }

  ngOnDestroy(): void {
  }
}
