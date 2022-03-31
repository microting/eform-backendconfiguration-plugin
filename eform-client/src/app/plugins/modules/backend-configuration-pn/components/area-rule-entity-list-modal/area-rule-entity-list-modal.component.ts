import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {AdvEntitySearchableItemModel, AdvEntitySelectableItemModel} from 'src/app/common/models';
import { EntityItemEditNameComponent } from 'src/app/common/modules/eform-shared/components';
import {EntitySelectService} from 'src/app/common/services';
import {getRandomInt} from 'src/app/common/helpers';
import * as R from 'ramda';

@Component({
  selector: 'app-area-rule-entity-list-modal',
  templateUrl: './area-rule-entity-list-modal.component.html',
  styleUrls: ['./area-rule-entity-list-modal.component.scss'],
})
export class AreaRuleEntityListModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @ViewChild('modalNameEdit', { static: true }) modalNameEdit: EntityItemEditNameComponent;
  @Output() entityListChanged: EventEmitter<Array<AdvEntitySelectableItemModel | AdvEntitySearchableItemModel>> =
    new EventEmitter<Array<AdvEntitySelectableItemModel | AdvEntitySearchableItemModel>>();
  @Output() modalHided: EventEmitter<void> = new EventEmitter<void>();
  entityList: Array<AdvEntitySelectableItemModel | AdvEntitySearchableItemModel> = [];

  constructor(
    private entitySelectService: EntitySelectService,
    ) {
  }

  ngOnInit() {}

  show(groupId?: number) {
    if(!groupId){
      this.frame.show();
    } else {
      this.entitySelectService.getEntitySelectableGroup(groupId)
        .subscribe(data => {
          if(data && data.success && data.model){
            this.entityList = data.model.entityGroupItemLst;
            this.entityList.forEach(x => {
              if(!x.tempId)
              {
                x.tempId = this.getRandId();
              }
            });
            this.frame.show();
          }
        })
    }
  }

  hide() {
    this.entityList = [];
    this.frame.hide();
    this.modalHided.emit();
  }

  onUpdateEntityList() {
    this.entityListChanged.emit(this.entityList);
  }

  addNewAdvEntitySelectableItem() {
    const item = new AdvEntitySelectableItemModel();
    item.entityItemUId = this.entityList.length.toString();
    item.tempId = this.getRandId();
    this.entityList.push(item);
  }

  onOpenEditNameModal(model: AdvEntitySelectableItemModel | AdvEntitySearchableItemModel) {
    this.frame.hide();
    this.modalNameEdit.show(model);
  }

  onItemUpdated(model: AdvEntitySelectableItemModel | AdvEntitySearchableItemModel) {
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
}
