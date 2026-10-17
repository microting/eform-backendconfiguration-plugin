import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {ArchiveNamedItem} from '../../../../models';
import {accessGroupItems, groupNames} from '../../../../helpers';

export interface ArchiveAccessDialogData {
  /** 'folder': the folder's own groups; 'files' (bulk, open with `ownAccess: false`): inherit or own groups. */
  mode: 'folder' | 'files';
  title: string;
  titleParams?: Record<string, string | number>;
  /** Live medarbejdergrupper from GET worker-tags (#1213 logic). */
  workerTags: CommonDictionaryModel[];
  /** The groups set today. */
  currentGroups: ArchiveNamedItem[];
  ownAccess: boolean;
  /**
   * Folder mode, subfolder of a restricted folder: the parent and its effective groups. With no own groups the
   * subfolder inherits these, so the hint names them instead of "everyone".
   */
  inheritedFrom?: {folder: string; groups: ArchiveNamedItem[]};
}

export interface ArchiveAccessDialogResult {
  ownAccess: boolean;
  tagIds: number[];
}

/** Læseadgang (admin only). No groups = everyone with archive access. */
@Component({
  selector: 'app-archive-access-dialog',
  templateUrl: './archive-access-dialog.component.html',
  standalone: false,
})
export class ArchiveAccessDialogComponent {
  readonly data: ArchiveAccessDialogData = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<ArchiveAccessDialogComponent, ArchiveAccessDialogResult>);
  private translateService = inject(TranslateService);

  /** Live groups, plus current groups without live members (kept selected, see accessGroupItems). */
  readonly items: ArchiveNamedItem[] = accessGroupItems(
    this.data.workerTags, this.data.currentGroups, this.translateService.instant('no members'));
  ownAccess = this.data.mode === 'folder' || this.data.ownAccess;
  tagIds: number[] = this.data.currentGroups.map(g => g.id);
  /** Bulk only: Save waits for a choice, so one click never widens access (C5). */
  pristine = this.data.mode === 'files';

  /** Shown while no group is chosen: what that means — the parent's groups when inherited, else everyone. */
  get openHint(): string {
    const parent = this.data.mode === 'folder' ? this.data.inheritedFrom : undefined;
    return parent?.groups.length
      ? this.translateService.instant('Inherits from {{folder}}: {{who}}', {folder: parent.folder, who: groupNames(parent.groups)})
      : this.translateService.instant('No groups: everyone with archive access can read.');
  }

  get canSave(): boolean {
    return !this.pristine;
  }

  touch(): void {
    this.pristine = false;
  }

  save(): void {
    if (this.canSave) {
      this.dialogRef.close({ownAccess: this.ownAccess, tagIds: this.ownAccess ? this.tagIds : []});
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
