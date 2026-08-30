import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';
import {CalendarBoardModel} from '../../../../../models/calendar';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskListService,
} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

// Copy the selected tasks onto a board that belongs to a (possibly
// different) target property. Board + worker roster are both scoped to
// whichever property is picked, mirroring task-list-page's own
// onPropertyChanged (loadBoards + loadWorkers). A worker is required because
// the backend's copy endpoint enforces AtLeastOneWorkerMustBeAssigned +
// PropertyWorkers validation on the target site.
@Component({
  standalone: false,
  selector: 'app-batch-copy-modal',
  templateUrl: './batch-copy-modal.component.html',
})
export class BatchCopyModalComponent {
  targetPropertyId: number | null = null;
  targetBoardId: number | null = null;
  siteId: number | null = null;
  startDate: Date = new Date();
  // KEPT at today. #1122 lifted the past-date guard for CHANGE START DATE, not
  // for COPY: copy CREATES new plannings on a target property, and
  // BackendConfigurationTaskListService.Copy still resolves the copy's first
  // occurrence against "now". Relaxing this floor is a separate decision about
  // the copy endpoint, not collateral of the batch re-anchor action.
  minDate: Date = new Date();

  boards: CalendarBoardModel[] = [];
  workers: CommonDictionaryModel[] = [];

  constructor(
    public dialogRef: MatDialogRef<BatchCopyModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private calendarService: BackendConfigurationPnCalendarService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get valid(): boolean {
    return this.targetPropertyId != null
      && this.targetBoardId != null
      && this.siteId != null
      && this.startDate != null;
  }

  onPropertyChange(propertyId: number | null) {
    this.targetBoardId = null;
    this.siteId = null;
    this.boards = [];
    this.workers = [];
    if (propertyId == null) {
      return;
    }
    this.calendarService.getBoards(propertyId).subscribe(res => {
      if (res && res.success) {
        this.boards = res.model;
      }
    });
    this.propertiesService.getDeviceUsersFiltered({
      propertyIds: [propertyId],
      nameFilter: '',
      sort: 'Name',
      isSortDsc: false,
      showResigned: false,
      tagIds: [],
    }).subscribe(res => {
      if (res && res.success) {
        this.workers = res.model.map(u => ({
          id: u.siteId,
          name: u.fullName || `${u.userFirstName} ${u.userLastName}`.trim() || u.siteName,
          description: '',
        } as CommonDictionaryModel));
      }
    });
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    const d = this.startDate;
    // Date-only "yyyy-MM-dd" via local getters (not toISOString) so the
    // picked calendar day survives a UTC offset unchanged — same reasoning
    // as TaskCreateEditModalComponent.onSave's dateStr.
    const startDate = `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
    this.taskListService.copy({
      taskIds,
      targetPropertyId: this.targetPropertyId!,
      targetBoardId: this.targetBoardId!,
      siteId: this.siteId!,
      startDate,
    }).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
