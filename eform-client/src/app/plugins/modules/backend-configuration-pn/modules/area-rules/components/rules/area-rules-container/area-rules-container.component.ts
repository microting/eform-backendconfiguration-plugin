import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AuthStateService } from 'src/app/common/store';
import { AreaRulePlanModalComponent } from 'src/app/plugins/modules/backend-configuration-pn/modules/area-rules/components';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
} from '../';
import {
  AreaRuleSimpleModel,
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleUpdateModel,
  AreaRuleModel,
} from '../../../../../models';
import { BackendConfigurationPnAreasService } from '../../../../../services/backend-configuration-pn-areas.service';

@AutoUnsubscribe()
@Component({
  selector: 'app-area-rules-container',
  templateUrl: './area-rules-container.component.html',
  styleUrls: ['./area-rules-container.component.scss'],
})
export class AreaRulesContainerComponent implements OnInit, OnDestroy {
  areaRules: AreaRuleSimpleModel[] = [];
  @ViewChild('createAreaRuleModal', { static: false })
  createAreaRuleModal: AreaRuleCreateModalComponent;
  @ViewChild('editAreaRuleModal', { static: false })
  editAreaRuleModal: AreaRuleEditModalComponent;
  @ViewChild('deleteAreaRuleModal', { static: false })
  deleteAreaRuleModal: AreaRuleDeleteModalComponent;
  @ViewChild('planAreaRuleModal', { static: false })
  planAreaRuleModal: AreaRulePlanModalComponent;

  areaRulesModel: AreaRuleSimpleModel[] = [];

  getAreaRulesSub$: Subscription;
  getSingleAreaRuleSub$: Subscription;
  createAreaRuleSub$: Subscription;
  editAreaRuleSub$: Subscription;
  deleteAreaRuleSub$: Subscription;
  planAreaRuleSub$: Subscription;

  constructor(
    private areaRulesService: BackendConfigurationPnAreasService,
    public authStateService: AuthStateService
  ) {}

  ngOnInit() {
    this.getAreaRules();
  }

  getAreaRules() {
    this.getAreaRulesSub$ = this.areaRulesService
      .getAreaRules(1)
      .subscribe((data) => {
        this.areaRulesModel = data.model;
      });
  }

  showAreaRule(rule: AreaRuleSimpleModel, modal: 'edit' | 'plan') {
    this.getSingleAreaRuleSub$ = this.areaRulesService
      .getSingleAreaRule(rule.id)
      .subscribe((data) => {
        if (modal === 'edit') {
          this.editAreaRuleModal.show(data.model);
        } else {
          this.planAreaRuleModal.show(data.model);
        }
      });
  }

  showAreaRuleCreateModal() {
    this.createAreaRuleModal.show();
  }

  showDeleteAreaRuleModal(rule: AreaRuleSimpleModel) {
    this.deleteAreaRuleModal.show(rule);
  }

  onCreateAreaRule(model: AreaRulesCreateModel) {
    this.createAreaRuleSub$ = this.areaRulesService
      .createAreaRules(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          this.createAreaRuleModal.hide();
        }
      });
  }

  onUpdateAreaRule(model: AreaRuleUpdateModel) {
    this.editAreaRuleSub$ = this.areaRulesService
      .updateAreaRule(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          this.editAreaRuleModal.hide();
        }
      });
  }

  onDeleteAreaRule(areaRuleId: number) {
    this.deleteAreaRuleSub$ = this.areaRulesService
      .deleteAreaRule(areaRuleId)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          this.deleteAreaRuleModal.hide();
        }
      });
  }

  onUpdateAreaRulePlan(rulePlanning: AreaRulePlanningModel) {
    this.planAreaRuleSub$ = this.areaRulesService
      .updateAreaRulePlanning(rulePlanning)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          this.planAreaRuleModal.hide();
        }
      });
  }

  ngOnDestroy(): void {}
}
