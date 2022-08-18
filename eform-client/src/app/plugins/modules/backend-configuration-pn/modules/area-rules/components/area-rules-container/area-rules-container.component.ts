import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AuthStateService } from 'src/app/common/store';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
} from '../';
import {
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleSimpleModel,
  AreaRuleUpdateModel,
  AreaModel,
  PropertyModel,
} from '../../../../models';
import {
  BackendConfigurationPnAreasService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import { TranslateService } from '@ngx-translate/core';
import {AreaRuleEntityListModalComponent, AreaRulePlanModalComponent} from '../../../../components';
import {AdvEntitySelectableItemModel, Paged} from 'src/app/common/models';
import {EntitySelectService} from 'src/app/common/services';
import {ChemicalModel} from 'src/app/plugins/modules/backend-configuration-pn/modules';
import {
  BackendConfigurationPnChemicalsService
} from 'src/app/plugins/modules/backend-configuration-pn/services/backend-configuration-pn-chemicals.service';
import {ChemicalsStateService} from 'src/app/plugins/modules/backend-configuration-pn/components/chemicals/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-area-rules-container',
  templateUrl: './area-rules-container.component.html',
  styleUrls: ['./area-rules-container.component.scss'],
})
export class AreaRulesContainerComponent implements OnInit, OnDestroy {
  @ViewChild('createAreaRuleModal', { static: false })
  createAreaRuleModal: AreaRuleCreateModalComponent;
  @ViewChild('editAreaRuleModal', { static: false })
  editAreaRuleModal: AreaRuleEditModalComponent;
  @ViewChild('deleteAreaRuleModal', { static: false })
  deleteAreaRuleModal: AreaRuleDeleteModalComponent;
  @ViewChild('planAreaRuleModal', { static: false })
  planAreaRuleModal: AreaRulePlanModalComponent;
  @ViewChild('entityListEditModal', { static: false })
  entityListEditModal: AreaRuleEntityListModalComponent;

  areaRules: AreaRuleSimpleModel[] = [];
  selectedArea: AreaModel = new AreaModel();
  propertyAreaId: number;
  selectedPropertyId: number;
  selectedProperty: PropertyModel;
  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    { name: '', href: '' },
    { name: '' },
  ];

  getAreaRulesSub$: Subscription;
  getAreaRulePlanningSub$: Subscription;
  getAreaSub$: Subscription;
  getSingleAreaRuleSub$: Subscription;
  createAreaRuleSub$: Subscription;
  editAreaRuleSub$: Subscription;
  deleteAreaRuleSub$: Subscription;
  planAreaRuleSub$: Subscription;
  getAllPropertiesDictionarySub$: Subscription;
  deleteAreaRulesSub$: Subscription;
  getTranslateSub$: Subscription;
  routerSub$: Subscription;
  chemicalsModel: Paged<ChemicalModel> = new Paged<ChemicalModel>();
  getChemicalsSub$: Subscription;

  constructor(
    private areasService: BackendConfigurationPnAreasService,
    public authStateService: AuthStateService,
    private route: ActivatedRoute,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    private translateService: TranslateService,
    private entitySelectService: EntitySelectService,
    public chemicalsStateService: ChemicalsStateService,
  ) {}

  ngOnInit() {
    this.routerSub$ = this.route.params.subscribe((params) => {
      this.getTranslateSub$ = this.translateService
        .get('Properties')
        .subscribe((translate) => (this.breadcrumbs[0].name = translate));
      this.propertyAreaId = +params['propertyAreaId'];
      this.selectedPropertyId = +params['propertyId'];
      this.getProperty(this.selectedPropertyId);
      this.getAreaRules(this.propertyAreaId);
      this.getArea(this.propertyAreaId);
    });
  }


  getChemicals() {
    this.getChemicalsSub$ = this.chemicalsStateService
      .getAllChemicals(this.selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          // map folder names to items
          if (data.model.total > 0) {
            this.chemicalsModel = {
              ...data.model,
              entities: data.model.entities.map((x) => {
                return {
                  ...x,
                };
              }),
            };
          } else {
            this.chemicalsModel = data.model;
          }
          // Required if page or anything else was changed
        }
      });
  }

  getArea(propertyAreaId: number) {
    this.getAreaSub$ = this.areasService
      .getAreaByPropertyAreaId(propertyAreaId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.selectedArea = operation.model;
          this.breadcrumbs[2] = { name: this.selectedArea.name };
          this.getChemicals();
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
        .getAreaRulePlanningByRuleId(rule.id)
        .subscribe((operation) => {
          this.planAreaRuleModal.show(
            rule,
            this.selectedPropertyId,
            this.selectedArea,
            operation.model
          );
        });
    } else {
      this.planAreaRuleModal.show(rule, this.selectedPropertyId, this.selectedArea);
    }
  }

  showEditAreaRuleModal(rule: AreaRuleSimpleModel) {
    this.getSingleAreaRuleSub$ = this.areasService
      .getSingleAreaRule(rule.id, this.selectedPropertyId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.editAreaRuleModal.show(operation.model, rule.planningStatus);
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

  private getProperty(selectedPropertyId: number) {
    this.getAllPropertiesDictionarySub$ = this.backendConfigurationPnPropertiesService
      .readProperty(selectedPropertyId)
      .subscribe((data) => {
        if (data && data.success) {
          this.selectedProperty = data.model;
          this.breadcrumbs[1] = {
            name: this.selectedProperty.name,
            href: `/plugins/backend-configuration-pn/property-areas/${this.selectedProperty.id}`,
          };
        }
      });
  }

  onDeleteAreaRules(areaRuleIds: number[]) {
    this.deleteAreaRulesSub$ = this.areasService
      .deleteAreaRules(areaRuleIds)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules(this.propertyAreaId);
          this.createAreaRuleModal.hide();
        }
      });
  }

  ngOnDestroy(): void {
  }

  updateEntityList(model: Array<AdvEntitySelectableItemModel>) {
    debugger;
    if(!this.selectedArea.groupId){
      this.backendConfigurationPnPropertiesService.createEntityList(model, this.propertyAreaId)
        .subscribe((x => {
          if(x.success){
            this.entityListEditModal.hide();
            this.getArea(this.propertyAreaId);
          }
        }))
    } else {
      this.entitySelectService.getEntitySelectableGroup(this.selectedArea.groupId)
      .subscribe(data => {
        if (data.success) {
          this.entitySelectService.updateEntitySelectableGroup({
            advEntitySelectableItemModels: model,
            groupUid: +data.model.microtingUUID,
            ...data.model
          }).subscribe(x => {
            if (x.success) {
              this.entityListEditModal.hide();
            }
          });
        }
      });
    }
  }
}
