import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {AdvEntitySearchableItemModel, AdvEntitySelectableItemModel} from 'src/app/common/models';
import { EntityItemEditNameComponent } from 'src/app/common/modules/eform-shared/components';
import {EntitySelectService} from 'src/app/common/services';

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

  show(groupId?: number, entityList?: AdvEntitySelectableItemModel[]) {
    if (!groupId && entityList){
      this.entityList = entityList;
      this.frame.show();
    } else if(!groupId && !entityList){
      this.frame.show();
    } else {
      this.entitySelectService.getEntitySelectableGroup(groupId)
        .subscribe(data => {
          if(data && data.success && data.model){
            this.entityList = data.model.entityGroupItemLst;
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
    this.hide();
  }

  addNewAdvEntitySelectableItem() {
    const item = new AdvEntitySelectableItemModel();
    item.entityItemUId = this.entityList.length.toString();
    this.entityList.push(item);
  }

  onOpenEditNameModal(model: AdvEntitySelectableItemModel | AdvEntitySearchableItemModel) {
    this.frame.hide();
    this.modalNameEdit.show(model);
  }

  onItemUpdated(model: AdvEntitySelectableItemModel | AdvEntitySearchableItemModel) {
    const index = this.entityList
      .findIndex(x => x.entityItemUId === model.entityItemUId);
    if (index !== -1) {
      this.entityList[index] = model;
    }
  }
}
