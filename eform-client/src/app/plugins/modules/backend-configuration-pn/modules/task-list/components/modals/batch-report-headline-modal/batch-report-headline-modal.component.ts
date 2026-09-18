import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services/items-planning-pn-tags.service';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

/**
 * Batch "Skift rapportoverskrift" modal (#1298) — sets the report headline of
 * every selected task.
 *
 * Product decisions (2026-09-18):
 *  - a headline is REQUIRED: there is no "no headline" option (a task without
 *    one disappears from Rapportering, #1301), so Save stays disabled until one
 *    is picked and the select is not clearable;
 *  - the dialog MAY create a new headline, exactly like the calendar modal's
 *    `calendarEventPlanningTag` select (`addPlanningTag` ->
 *    `ItemsPlanningPnTagsService.createPlanningTag`).
 *
 * Headlines (items-planning PlanningTags) are global, so `data.tags` is the
 * page's full tag list and the action needs no property filter.
 */
@Component({
  standalone: false,
  selector: 'app-batch-report-headline-modal',
  templateUrl: './batch-report-headline-modal.component.html',
})
export class BatchReportHeadlineModalComponent {
  itemPlanningTagId: number | null = null;
  // A field, never a getter: mtx-select treats a fresh [items] array per
  // change-detection tick as an input change and rebuilds its options forever.
  // Replaced (not mutated) when a headline is created so the select sees it.
  tags: {id: number; name: string}[];

  constructor(
    public dialogRef: MatDialogRef<BatchReportHeadlineModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
    private tagsService: ItemsPlanningPnTagsService,
    private toastr: ToastrService,
    private translate: TranslateService,
  ) {
    this.tags = (data.tags ?? []).map(t => ({id: t.id, name: t.name}));
  }

  get valid(): boolean {
    return this.itemPlanningTagId != null;
  }

  /**
   * ng-select `[addTag]` callback — same create call as the calendar modal's
   * `addPlanningTag`. Resolves with the created tag so ng-select can select it;
   * the id is also set explicitly (the calendar modal's belt-and-braces for the
   * MatFormField/OnPush corner case where the bound value never arrives).
   */
  addTag = (name: string): Promise<{id: number; name: string}> =>
    new Promise((resolve, reject) => {
      const trimmed = (name ?? '').trim();
      if (!trimmed) {
        reject();
        return;
      }
      this.tagsService.createPlanningTag({name: trimmed}).subscribe({
        next: res => {
          if (res && res.success && res.model) {
            const tag = {id: res.model.id, name: res.model.name};
            if (!this.tags.some(t => t.id === tag.id)) {
              this.tags = [...this.tags, tag];
            }
            this.itemPlanningTagId = tag.id;
            resolve(tag);
          } else {
            this.toastr.error(this.translate.instant('Could not create report headline'));
            reject();
          }
        },
        error: () => {
          this.toastr.error(this.translate.instant('Could not create report headline'));
          reject();
        },
      });
    });

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService
      .changeReportHeadline({taskIds, itemPlanningTagId: this.itemPlanningTagId!})
      .subscribe(res => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}
