import {
  AfterViewChecked,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import {WorkOrderCaseModel} from '../../../../models';
import {TaskManagementStateService} from '../store';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {TaskManagementPrioritiesEnum} from '../../../../enums';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementPaginationIsSortDsc,
  selectTaskManagementPaginationSort
} from '../../../../state/task-management/task-management.selector';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnTaskManagementService} from '../../../../services';
import {
  TaskManagementCreateShowModalComponent,
  TaskManagementDeleteModalComponent
} from '../';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {Gallery, GalleryItem, ImageItem} from 'ng-gallery';
import {Lightbox} from 'ng-gallery/lightbox';
import {TemplateFilesService} from 'src/app/common/services';
import {forkJoin} from 'rxjs';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-management-table',
    templateUrl: './task-management-table.component.html',
    styleUrls: ['./task-management-table.component.scss'],
    standalone: false
})
export class TaskManagementTableComponent implements OnInit, OnDestroy, OnChanges, AfterViewChecked {
  private store = inject(Store);
  public taskManagementStateService = inject(TaskManagementStateService);
  private translateService = inject(TranslateService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private el = inject(ElementRef);
  private taskManagementService = inject(BackendConfigurationPnTaskManagementService);
  private imageService = inject(TemplateFilesService);
  public gallery = inject(Gallery);
  public lightbox = inject(Lightbox);

  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Id'), field: 'id', sortProp: {id: 'Id'}, sortable: true, class: 'id'},
    {
      header: this.translateService.stream('Created date'),
      field: 'caseInitiated',
      sortProp: {id: 'CaseInitiated'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy HH:mm', timezone: 'utc'},
      class: 'createdDate'
    },
    {header: this.translateService.stream('Property'), field: 'propertyName', sortProp: {id: 'PropertyName'}, sortable: true, class: 'propertyName'},
    {header: this.translateService.stream('Area'), field: 'areaName', sortProp: {id: 'SelectedAreaName'}, sortable: true, class: 'areaName'},
    {header: this.translateService.stream('Created by'), field: 'createdByName', sortProp: {id: 'CreatedByName'}, sortable: true, class: 'createdByName'},
    {header: this.translateService.stream('Created by text'), field: 'createdByText', sortProp: {id: 'CreatedByText'}, sortable: true, class: 'createdByText'},
    {header: this.translateService.stream('Last assigned to'), field: 'lastAssignedTo', sortProp: {id: 'LastAssignedToName'}, sortable: true, class: 'lastAssignedTo'},
    {
      header: this.translateService.stream('Description'),
      field: 'description',
      formatter: (rowData: WorkOrderCaseModel) => rowData.description,
      class: 'description',
    },
    {
      header: this.translateService.stream('Photo'),
      field: 'pictureNames',
      width: '70px',
    },
    {
      header: this.translateService.stream('Last update date'),
      field: 'lastUpdateDate',
      sortProp: {id: 'UpdatedAt'},
      sortable: true,
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy HH:mm', timezone: 'utc'},
      class: 'lastUpdateDate'
    },
    {header: this.translateService.stream('Last update by'), field: 'lastUpdatedBy', sortProp: {id: 'LastUpdatedByName'}, sortable: true, class: 'lastUpdatedBy'},
    {header: this.translateService.stream('Priority'),
      field: 'priority',
      sortProp: {id: 'Priority'},
      sortable: true,
      class: 'priority',
    },
    {
      header: this.translateService.stream('Status'),
      field: 'status',
      sortProp: {id: 'CaseStatusesEnum'},
      sortable: true,
      class: 'status'
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      width: '100px',
      pinned: 'right',
      right: '0px',
    },
  ];

  @Input() workOrderCases: WorkOrderCaseModel[];
  @Input() highlightedId: string | null = null;
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() updateTableWithHighlight: EventEmitter<string> = new EventEmitter<string>();
  @Output() highlightedRowRendered: EventEmitter<void> = new EventEmitter<void>();

  public selectTaskManagementPaginationSort$ = this.store.select(selectTaskManagementPaginationSort);
  public selectTaskManagementPaginationIsSortDsc$ = this.store.select(selectTaskManagementPaginationIsSortDsc);

  getWorkOrderCaseSub$: Subscription;
  viewPicturesSub$: Subscription;

  private pendingScrollGroupId: string | null = null;
  private waitingForFreshData = false;

  ngOnInit(): void {
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['highlightedId'] && this.highlightedId !== null && this.highlightedId !== undefined) {
      this.pendingScrollGroupId = null;
      this.waitingForFreshData = true;
    }
    if (changes['workOrderCases'] && this.waitingForFreshData && this.highlightedId !== null && this.highlightedId !== undefined) {
      this.waitingForFreshData = false;
      this.pendingScrollGroupId = this.highlightedId;
    }
  }

  ngAfterViewChecked(): void {
    if (this.pendingScrollGroupId === null || !this.workOrderCases?.length) return;
    const groupId = this.pendingScrollGroupId;
    const cell = this.el.nativeElement.querySelector(`#taskId-${groupId}`);
    if (!cell) return;
    this.pendingScrollGroupId = null;
    setTimeout(() => {
      const freshCell = this.el.nativeElement.querySelector(`#taskId-${groupId}`);
      if (freshCell) {
        const tr = freshCell.closest('tr') || freshCell.closest('mat-row') || freshCell.parentElement;
        if (tr) {
          tr.scrollIntoView({behavior: 'smooth', block: 'center'});
          tr.classList.add('highlight-row');
          setTimeout(() => tr.classList.remove('highlight-row'), 5000);
        }
      }
      this.highlightedRowRendered.emit();
    });
  }

  sortTable(sort: Sort) {
    this.taskManagementStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  openViewModal(workOrderCaseId: number) {
    const groupId = this.workOrderCases.find(c => c.id === workOrderCaseId)?.groupId;
    this.getWorkOrderCaseSub$ = this.taskManagementService
      .getWorkOrderCase(workOrderCaseId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.dialog.open(TaskManagementCreateShowModalComponent, {
            ...dialogConfigHelper(this.overlay, data.model),
            panelClass: 'task-management-modal'
          }).afterClosed().subscribe(result => {
            if (result && groupId) {
              this.updateTableWithHighlight.emit(groupId);
            }
          });
        }
      });
  }

  openDeleteModal(workOrderCaseModel: WorkOrderCaseModel) {
    this.dialog.open(TaskManagementDeleteModalComponent, dialogConfigHelper(this.overlay, workOrderCaseModel))
      .afterClosed().subscribe(result => {
        if (result) {
          this.updateTable.emit();
        }
      });
  }

  onViewPictures(caseId: number) {
    this.viewPicturesSub$ = this.taskManagementService.getWorkOrderCase(caseId)
      .subscribe(res => {
        if (!res?.success || !res.model?.pictureNames?.length) return;
        const names = res.model.pictureNames as string[];
        forkJoin(names.map(name => this.imageService.getImage(name)))
          .subscribe((blobs: Blob[]) => {
            const galleryImages: GalleryItem[] = blobs.map((blob, i) => {
              const url = URL.createObjectURL(blob);
              return new ImageItem({src: url, thumb: url});
            });
            const ref = this.gallery.ref('lightbox', {
              counter: galleryImages.length > 1,
              counterPosition: 'bottom'
            });
            ref.load(galleryImages);
            this.lightbox.open(0);
          });
      });
  }

  rowClassFormatter = {};

  protected readonly TaskManagementPrioritiesEnum = TaskManagementPrioritiesEnum;

  priorityClassMap = {
    [TaskManagementPrioritiesEnum.Urgent]: 'priority-urgent',
    [TaskManagementPrioritiesEnum.High]: 'priority-high',
    [TaskManagementPrioritiesEnum.Medium]: 'priority-medium',
    [TaskManagementPrioritiesEnum.Low]: 'priority-low',
  };

  getPriorityClass(priority: number): string {
    return this.priorityClassMap[priority] || '';
  }

  ngOnDestroy(): void {
  }
}
