import {Component, EventEmitter, Inject, OnDestroy, OnInit,} from '@angular/core';
import {EntityItemModel} from 'src/app/common/models';
import { EntityItemEditNameComponent } from 'src/app/common/modules/eform-shared/components';
import {EntitySelectService} from 'src/app/common/services';
import {dialogConfigHelper, getRandomInt} from 'src/app/common/helpers';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';

@AutoUnsubscribe()
@Component({
    selector: 'app-area-rule-entity-list-modal',
    templateUrl: './area-rule-entity-list-modal.component.html',
    styleUrls: ['./area-rule-entity-list-modal.component.scss'],
    standalone: false
})
export class AreaRuleEntityListModalComponent implements OnInit, OnDestroy {
  entityListChanged: EventEmitter<Array<EntityItemModel>> = new EventEmitter<Array<EntityItemModel>>();
  entityList: Array<EntityItemModel> = [];

  entityItemEditNameComponentAfterClosedSub$: Subscription;
  getEntitySelectableGroupSub$: Subscription;

  constructor(
    private entitySelectService: EntitySelectService,
    public dialog: MatDialog,
    private overlay: Overlay,
    public dialogRef: MatDialogRef<AreaRuleEntityListModalComponent>,
    @Inject(MAT_DIALOG_DATA) groupId?: number
    ) {
    if (groupId) {
      this.getEntitySelectableGroupSub$ = this.entitySelectService.getEntitySelectableGroup(groupId)
        .subscribe(data => {
          if (data && data.success && data.model) {
            this.entityList = [...data.model.entityGroupItemLst];
            this.entityList.forEach(x => {
              if (!x.tempId)
              {
                x.tempId = this.getRandId();
              }
            });
          }
        })
    }
  }

  ngOnInit() {}

  hide() {
    this.dialogRef.close();
    this.entityList = [];
  }

  onUpdateEntityList() {
    this.entityListChanged.emit(this.entityList);
  }

  addNewAdvEntitySelectableItem() {
    const item = new EntityItemModel();
    item.entityItemUId = this.entityList.length.toString();
    item.displayIndex = this.entityList.length;
    item.tempId = this.getRandId();
    this.entityList.push(item);
  }

  onOpenEditNameModal(model: EntityItemModel) {
    // this.modalNameEdit.show(model);
    this.entityItemEditNameComponentAfterClosedSub$ = this.dialog.open(EntityItemEditNameComponent,
      {...dialogConfigHelper(this.overlay, model), minWidth: 500})
      .afterClosed().subscribe(result => result.result ? this.onItemUpdated(result.data) : undefined);
  }

  onItemUpdated(model: EntityItemModel) {
    const index = this.entityList.findIndex(x => x.tempId === model.tempId);
    if (index !== -1) {
      this.entityList[index] = model;
    }
  }

  getRandId(): number{
    const randId = getRandomInt(1, 1000);
    if(this.entityList.findIndex(x => x.tempId === randId) !== -1){
      return this.getRandId();
    }
    return randId;
  }

  ngOnDestroy(): void {
  }
}
