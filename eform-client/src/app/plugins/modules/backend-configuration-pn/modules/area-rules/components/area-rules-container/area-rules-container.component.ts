import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AuthStateService } from 'src/app/common/store';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
  AreaRulePlanModalComponent,
} from '../index';
import {
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleSimpleModel,
  AreaRuleUpdateModel,
  AreaModel,
  AreaRuleT5Model,
} from '../../../../models';
import { BackendConfigurationPnAreasService } from '../../../../services/backend-configuration-pn-areas.service';

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

  selectedArea: AreaModel = new AreaModel();
  propertyAreaId: number;
  selectedPropertyId: number;

  getAreaRulesSub$: Subscription;
  getAreaRulePlanningSub$: Subscription;
  getAreaSub$: Subscription;
  getSingleAreaRuleSub$: Subscription;
  createAreaRuleSub$: Subscription;
  editAreaRuleSub$: Subscription;
  deleteAreaRuleSub$: Subscription;
  planAreaRuleSub$: Subscription;

  constructor(
    private areasService: BackendConfigurationPnAreasService,
    public authStateService: AuthStateService,
    private route: ActivatedRoute
  ) {}

  ngOnInit() {
    this.route.params.subscribe((params) => {
      this.propertyAreaId = +params['propertyAreaId'];
      this.selectedPropertyId = +params['propertyId'];
      this.getAreaRules(this.propertyAreaId);
      this.getArea(this.propertyAreaId);
    });
  }

  getArea(propertyAreaId: number) {
    this.getAreaSub$ = this.areasService
      .getArea(propertyAreaId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.selectedArea = operation.model;
        }
      });
  }

  getAreaRules(propertyAreaId: number) {
    this.getAreaRulesSub$ = this.areasService
      .getAreaRules(propertyAreaId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.areaRules = operation.model;
        }
      });
  }

  showPlanAreaRuleModal(rule: AreaRuleSimpleModel) {
    if (rule.id) {
      this.getAreaRulePlanningSub$ = this.areasService
        .getAreaRulePlanning(rule.id)
        .subscribe((operation) => {
          this.planAreaRuleModal.show(rule, operation.model);
        });
    } else {
      this.planAreaRuleModal.show(rule);
    }
  }

  showEditAreaRuleModal(rule: AreaRuleSimpleModel) {
    this.getSingleAreaRuleSub$ = this.areasService
      .getSingleAreaRule(rule.id, this.selectedPropertyId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.editAreaRuleModal.show(operation.model);
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
    this.createAreaRuleSub$ = this.areasService
      .createAreaRules({ ...model, propertyAreaId: this.propertyAreaId })
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules(this.propertyAreaId);
          this.createAreaRuleModal.hide();
        }
      });
  }

  onUpdateAreaRule(model: AreaRuleUpdateModel) {
    this.editAreaRuleSub$ = this.areasService
      .updateAreaRule(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules(this.propertyAreaId);
          this.editAreaRuleModal.hide();
        }
      });
  }

  onDeleteAreaRule(areaRuleId: number) {
    this.deleteAreaRuleSub$ = this.areasService
      .deleteAreaRule(areaRuleId)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules(this.propertyAreaId);
          this.deleteAreaRuleModal.hide();
        }
      });
  }

  onUpdateAreaRulePlan(rulePlanning: AreaRulePlanningModel) {
    this.planAreaRuleSub$ = this.areasService
      .updateAreaRulePlanning(rulePlanning)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules(this.propertyAreaId);
          this.planAreaRuleModal.hide();
        }
      });
  }

  ngOnDestroy(): void {}
}
