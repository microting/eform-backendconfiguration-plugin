import {Component, Input, OnChanges, OnDestroy, SimpleChanges} from '@angular/core';
import {
  DocumentUpdatedDaysModel,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';

@AutoUnsubscribe()
@Component({
  selector: 'app-document-updated-days',
  templateUrl: './document-updated-days.component.html',
  styleUrls: ['./document-updated-days.component.scss'],
})
export class DocumentUpdatedDaysComponent implements OnChanges, OnDestroy {
  @Input() documentUpdatedDaysModel: DocumentUpdatedDaysModel;
  @Input() selectedPropertyName: string = '';
  @Input() view: number[] = [];
  chartData: { name: string, value: number }[] = [];
  colorSchemeLight = {
    domain: ['#ff0000', '#0000ff']
  };
  customColorsLight = [
    {
      name: this.translateService.instant('Exceeded'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('1-7 days'),
      value: '#0000ff',
    },
  ];
  colorSchemeDark = {
    domain: ['#ff0000', '#0000ff']
  };
  customColorsDark = [
    {
      name: this.translateService.instant('Exceeded'),
      value: '#ff0000',
    },
    {
      name: this.translateService.instant('1-7 days'),
      value: '#0000ff',
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

  constructor(
    private translateService: TranslateService,
    private authStateService: AuthStateService
  ) {
    this.isDarkThemeAsyncSub$ = authStateService.isDarkThemeAsync
      .subscribe(isDarkTheme => this.isDarkTheme = isDarkTheme);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes.documentUpdatedDaysModel &&
      !changes.documentUpdatedDaysModel.isFirstChange() &&
      changes.documentUpdatedDaysModel.currentValue) {
      this.chartData = [
        {
          name: this.translateService.instant('Exceeded or today'),
          value: this.documentUpdatedDaysModel.exceededOrToday,
        },
        {
          name: this.translateService.instant('Under 30 days'),
          value: this.documentUpdatedDaysModel.underThirtiethDays,
        },
      ];
    }
  }

  ngOnDestroy(): void {
  }
}
