import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges} from '@angular/core';
import {
  PlannedTaskDaysModel,
} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';

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

  constructor(
    private translateService: TranslateService,
    private authStateService: AuthStateService
  ) {
    this.isDarkThemeAsyncSub$ = authStateService.isDarkThemeAsync
      .subscribe(isDarkTheme => this.isDarkTheme = isDarkTheme);
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
    }
  }

  ngOnDestroy(): void {
  }
}
