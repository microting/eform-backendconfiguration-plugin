import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges,
  inject
} from '@angular/core';
import {DocumentUpdatedDaysModel,} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {DocumentsExpirationFilterEnum} from '../../../../enums';
import {selectCurrentUserLocale, selectIsDarkMode} from 'src/app/state';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-document-updated-days',
    templateUrl: './document-updated-days.component.html',
    styleUrls: ['./document-updated-days.component.scss'],
    standalone: false
})
export class DocumentUpdatedDaysComponent implements OnChanges, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);

  @Input() documentUpdatedDaysModel: DocumentUpdatedDaysModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  @Output() clickOnDiagram: EventEmitter<DocumentsExpirationFilterEnum | null> = new EventEmitter<DocumentsExpirationFilterEnum | null>();
  chartData: { name: string, value: number }[] = [];
  xAxisTicks: any[] = [];
  labels: string[] = ['Exceeded or today', 'Under 30 days', 'Over 30 days'];
  labelsTranslated: string[] = [];
  colorSchemeLight = {
    domain: ['#ff0000', '#0000ff', '#0000ff']
  };
  customColorsLight = [
    {
      name: this.labels[0],
      value: '#ff0000',
    },
    {
      name: this.labels[1],
      value: '#0000ff',
    },
    {
      name: this.labels[2],
      value: '#0000ff',
    },
  ];
  colorSchemeDark = {
    domain: ['#ff0000', '#0000ff', '#0000ff']
  };
  customColorsDark = [
    {
      name: this.labels[0],
      value: '#ff0000',
    },
    {
      name: this.labels[1],
      value: '#0000ff',
    },
    {
      name: this.labels[2],
      value: '#0000ff',
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


  ngOnChanges(changes: SimpleChanges): void {
    if (changes.documentUpdatedDaysModel &&
      !changes.documentUpdatedDaysModel.isFirstChange() &&
      changes.documentUpdatedDaysModel.currentValue) {
      this.changeData(this.labelsTranslated.length ? this.labelsTranslated : this.labels, this.documentUpdatedDaysModel);
      this.subToGetTranslates();
      this.getxAxisTicks();
    }
  }

  onClickOnDiagram(chartData: { name: string, value: number } = null) {
    if (!chartData) {
      this.clickOnDiagram.emit();
    } else {
      switch (chartData.name) {
        case this.translateService.instant('Exceeded or today'): {
          this.clickOnDiagram.emit(DocumentsExpirationFilterEnum.exceededOrToday);
          break;
        }
        case this.translateService.instant('Under 30 days'): {
          this.clickOnDiagram.emit(DocumentsExpirationFilterEnum.underThirtiethDays);
          break;
        }
        case this.translateService.instant('Over 30 days'): {
          this.clickOnDiagram.emit(DocumentsExpirationFilterEnum.overThirtiethDays);
          break;
        }
        default:
          this.clickOnDiagram.emit();
      }
    }
  }

  changeData(labelsTranslated: string[], documentUpdatedDaysModel: DocumentUpdatedDaysModel) {
    this.chartData = [
      {
        name: labelsTranslated[0],
        value: documentUpdatedDaysModel.exceededOrToday,
      },
      {
        name: labelsTranslated[1],
        value: documentUpdatedDaysModel.underThirtiethDays,
      },
      {
        name: labelsTranslated[2],
        value: documentUpdatedDaysModel.overThirtiethDays,
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
    ];
  }

  subToGetTranslates() {
    if (!this.selectCurrentUserLocaleSub$) {
      this.selectCurrentUserLocaleSub$ = this.selectCurrentUserLocale$.subscribe(() => {
        const x = this.translateService.instant(this.labels);
        this.labelsTranslated = Object.values(x);
        this.changeData(this.labelsTranslated, this.documentUpdatedDaysModel
          || {exceededOrToday: 0, overThirtiethDays: 0, underThirtiethDays: 0});
      });
    }
  }

  ngOnDestroy(): void {
  }
}
