import {Injectable} from '@angular/core';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {Observable, defaultIfEmpty, map, of, switchMap} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ArchiveFolderModel, ArchiveNamedItem} from '../../../models';
import {BackendConfigurationPnFilesService} from '../../../services';
import {ArchiveMovableFile, ArchiveMoveImpact, groupNames, moveImpact} from '../../../helpers';
import {
  ArchiveMoveConfirmData,
  ArchiveMoveConfirmDialogComponent,
} from '../components/archive-move-confirm-dialog/archive-move-confirm-dialog.component';

/**
 * Moving documents to a folder — from drag-and-drop, the selection bar and the document panel alike:
 * work out whether read access changes (D11), confirm with an admin or Leder, or stop anyone else (D17),
 * call PUT files/folder, then say what happened. Provided by FilesModule, so its dialogs open in
 * the module's injector.
 */
@Injectable()
export class ArchiveMoveFlowService {
  constructor(
    private dialog: MatDialog,
    private overlay: Overlay,
    private filesService: BackendConfigurationPnFilesService,
    private translateService: TranslateService,
    private toastrService: ToastrService,
  ) {}

  /**
   * Emits exactly once: true once the server moved the documents; false when nothing moved (cancelled, refused or
   * failed). The host interceptor turns a 404 (target folder gone or unreadable) into an empty completion, not an
   * error — `defaultIfEmpty(false)` makes that "not moved" too, so callers always get an answer.
   */
  move(files: ArchiveMovableFile[], folders: ArchiveFolderModel[], targetFolderId: number, canManage: boolean): Observable<boolean> {
    const toMove = files.filter(f => f.folderId !== targetFolderId);
    if (!toMove.length) {
      return of(false);
    }
    const impact = moveImpact(toMove, folders, targetFolderId);
    const confirmed$ = impact.changed === 0 ? of(true) : this.confirm(impact, canManage);
    return confirmed$.pipe(
      switchMap(confirmed => confirmed ? this.moveFiles(toMove, targetFolderId, impact) : of(false)),
      defaultIfEmpty(false),
    );
  }

  /** "3 dokumenter kan nu læses af alle" / "… af Ledelse, Bogholderi". */
  accessChangedText(count: number, groups: ArchiveNamedItem[]): string {
    return this.translateService.instant(
      count === 1 ? '{{count}} document can now be read by {{who}}' : '{{count}} documents can now be read by {{who}}',
      {count, who: groups.length ? groupNames(groups) : this.translateService.instant('everyone')},
    );
  }

  /** PUT files/folder, then the "moved" toast and, when access changed, who can read the documents now. */
  private moveFiles(toMove: ArchiveMovableFile[], targetFolderId: number, impact: ArchiveMoveImpact): Observable<boolean> {
    return this.filesService.moveFiles({fileIds: toMove.map(f => f.id), folderId: targetFolderId}).pipe(
      map(res => {
        if (!res?.success || !res.model) {
          return false;
        }
        const moved = res.model.moved;
        this.toastrService.success(this.translateService.instant(
          moved === 1 ? '{{count}} document moved' : '{{count}} documents moved', {count: moved}));
        if (res.model.accessChanged > 0) {
          this.toastrService.info(this.accessChangedText(res.model.accessChanged, impact.targetGroups));
        }
        return true;
      }),
    );
  }

  /** Admin or Leder: D11 confirm. Anyone else: the same dialog without a confirm button (D17); it always ends in false. */
  private confirm(impact: ArchiveMoveImpact, canManage: boolean): Observable<boolean> {
    const data: ArchiveMoveConfirmData = {text: this.accessChangedText(impact.changed, impact.targetGroups), canConfirm: canManage};
    return this.dialog
      .open(ArchiveMoveConfirmDialogComponent, {...dialogConfigHelper(this.overlay, data), minWidth: 420})
      .afterClosed()
      .pipe(map(result => canManage && result === true));
  }
}
