import {
  AfterViewChecked,
  Component, ElementRef, EventEmitter, Input,
  OnChanges,
  OnDestroy,
  OnInit, Output, SimpleChanges,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MtxGridColumn, MtxGridRowClassFormatter} from '@ng-matero/extensions/grid';
import {TaskWizardModel} from '../../../../models';
import {RepeatTypeEnum, TaskWizardStatusesEnum} from '../../../../enums';
import {TranslateService} from '@ngx-translate/core';
import {AuthStateService} from 'src/app/common/store';
import {format} from 'date-fns';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TaskWizardStateService} from '../store';
import {Sort} from '@angular/material/sort';
import {Store} from '@ngrx/store';
import {
  selectTaskWizardPaginationIsSortDsc,
  selectTaskWizardPaginationSort
} from '../../../../state';
import {selectAuthIsAdmin} from "src/app/state";
import {PlanningModel} from "src/app/plugins/modules/items-planning-pn/models";

@AutoUnsubscribe()
@Component({
    selector: 'app-task-wizard-table',
    templateUrl: './task-wizard-table.component.html',
    styleUrls: ['./task-wizard-table.component.scss'],
    standalone: false
})
export class TaskWizardTableComponent implements OnInit, OnChanges, AfterViewChecked, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  private authStateService = inject(AuthStateService);
  public taskWizardStateService = inject(TaskWizardStateService);
  private el = inject(ElementRef);

  @Input() tasks: TaskWizardModel[] = [];
  @Input() set highlightId(value: number | undefined) {
    if (value) {
      this._highlightId = value;
    }
  }
  _highlightId: number | null = null;
  private _needsScroll = false;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() deleteTask: EventEmitter<TaskWizardModel> = new EventEmitter<TaskWizardModel>();
  @Output() copyTask: EventEmitter<TaskWizardModel> = new EventEmitter<TaskWizardModel>();
  @Output() editTask: EventEmitter<TaskWizardModel> = new EventEmitter<TaskWizardModel>();
  @Input() selectedColCheckboxes: number[] = [];
  @Output() selectedPlanningsChanged: EventEmitter<number[]> = new EventEmitter<number[]>();
  tableHeaders: MtxGridColumn[] = [
    {field: 'id', header: this.translateService.stream('Id'), sortable: true, sortProp: {id: 'Id'},
      formatter: (model: TaskWizardModel) => model.id + ' <small class="microting-uid">(' + model.planningId + ')</small>',
    },
    {
      field: 'property',
      header: this.translateService.stream('Location'),
      sortable: true,
      sortProp: {id: 'Property'}
    },
    {
      field: 'folder',
      header: this.translateService.stream('Folder'),
      sortable: true,
      sortProp: {id: 'Folder'},
    },
    {field: 'tags', header: this.translateService.stream('Tags')},
    {field: 'tagReport', header: this.translateService.stream('Report tag')},
    {field: 'taskName', header: this.translateService.stream('Task name'), sortable: true, sortProp: {id: 'TaskName'}},
    {field: 'eform', header: this.translateService.stream('eForm'), sortable: true, sortProp: {id: 'Eform'},

      formatter: (model: TaskWizardModel) => model.eform + ' <small class="microting-uid">(' + model.eformId + ')</small>'
    },
    {
      field: 'startDate',
      header: this.translateService.stream('Start date'),
      sortable: true,
      sortProp: {id: 'StartDate'},
    },
    {
      field: 'repeat',
      header: this.translateService.stream('Repeat'),
    },
    {
      field: 'status',
      header: this.translateService.stream('Status'),
      sortable: true,
      sortProp: {id: 'Status'},
    },
    {
      field: 'assignedTo',
      header: this.translateService.stream('Assigned to'),
      sortable: false,
      sortProp: {id: 'AssignedTo'},
      formatter: (model: TaskWizardModel) => model.assignedTo.join('<br/>')
    },
    { field: 'actions', header: this.translateService.stream('Actions'), pinned: 'right' }
  ];
  public selectTaskWizardPaginationSort$ = this.store.select(selectTaskWizardPaginationSort);
  public selectTaskWizardPaginationIsSortDsc$ = this.store.select(selectTaskWizardPaginationIsSortDsc);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);
  rowClassFormatter: MtxGridRowClassFormatter = {
    highlighted: (row: TaskWizardModel) => !!this._highlightId && row.id === this._highlightId,
  };

  get TaskWizardStatusesEnum() {
    return TaskWizardStatusesEnum;
  }

  get RepeatTypeEnum() {
    return RepeatTypeEnum;
  }

  

  ngOnInit(): void {
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['tasks'] && this._highlightId) {
      this._needsScroll = true;
    }
  }

  ngAfterViewChecked(): void {
    if (this._needsScroll && this._highlightId && this.tasks?.length) {
      const rowIndex = this.tasks.findIndex(t => t.id === this._highlightId);
      if (rowIndex >= 0) {
        this._needsScroll = false;
        setTimeout(() => {
          const highlightedRow = this.el.nativeElement.querySelector('tr.highlighted');
          if (highlightedRow) {
            highlightedRow.scrollIntoView({behavior: 'smooth', block: 'center'});
          }
          setTimeout(() => {
            this._highlightId = null;
          }, 2000);
        }, 300);
      }
    }
  }

  ngOnDestroy(): void {
  }

  getFormattedStartDate(row: TaskWizardModel) {
    return format(row.startDate, 'P', {locale: this.authStateService.dateFnsLocale});
  }

  getFormattedDate(date: Date) {
    return format(date, 'P', {locale: this.authStateService.dateFnsLocale});
  }

  onClickTag(tag: CommonDictionaryModel) {
    this.taskWizardStateService.addTagToFilters(tag);
    // this.updateTable.emit();
  }

  onSortTable(sort: Sort) {
    this.taskWizardStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  updateSelectedPlannings(planningModels: TaskWizardModel[]) {
    this.selectedColCheckboxes = planningModels.map(x => x.id);
    this.selectedPlanningsChanged.emit(this.selectedColCheckboxes);
  }
}
