import {Component, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {AuthStateService} from 'src/app/common/store';
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
  ChemicalModel,
} from '../../../../models';
import {
  BackendConfigurationPnAreasService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {TranslateService} from '@ngx-translate/core';
import {AreaRuleEntityListModalComponent, AreaRulePlanModalComponent} from '../../../../components';
import {EntityItemModel, Paged} from 'src/app/common/models';
import {EntitySelectService} from 'src/app/common/services';
// import {ChemicalsStateService} from '../../../../components/chemicals/store';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AreaRulesStateService} from '../store';
import { Sort } from '@angular/material/sort';

@AutoUnsubscribe()
@Component({
    selector: 'app-area-rules-container',
    templateUrl: './area-rules-container.component.html',
    styleUrls: ['./area-rules-container.component.scss'],
    standalone: false
})
export class AreaRulesContainerComponent implements OnInit, OnDestroy {
  private areasService = inject(BackendConfigurationPnAreasService);
  public authStateService = inject(AuthStateService);
  private route = inject(ActivatedRoute);
  private backendConfigurationPnPropertiesService = inject(BackendConfigurationPnPropertiesService);
  private translateService = inject(TranslateService);
  private entitySelectService = inject(EntitySelectService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private areaRulesStateService = inject(AreaRulesStateService);

  areaRules: AreaRuleSimpleModel[] = [];
  selectedArea: AreaModel = new AreaModel();
  chemicalsModel: Paged<ChemicalModel> = new Paged<ChemicalModel>();
  propertyAreaId: number;
  selectedPropertyId: number;
  selectedProperty: PropertyModel;
  breadcrumbs = [
    {
      name: '',
      href: '/plugins/backend-configuration-pn/properties',
    },
    {name: '', href: ''},
    {name: ''},
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
  getChemicalsSub$: Subscription;
  updateAreaRulePlanSub$: Subscription;
  onDeleteAreaRuleSub$: Subscription;
  onCreateAreaRuleSub$: Subscription;
  onDeleteAreaRuleFromModalSub$: Subscription;
  onUpdateAreaRuleSub$: Subscription;
  propertyUpdateSub$: Subscription;

  

  ngOnInit() {
    this.routerSub$ = this.route.params.subscribe((params) => {
      this.getTranslateSub$ = this.translateService
        .get('Properties')
        .subscribe((translate) => (this.breadcrumbs[0].name = translate));
      this.propertyAreaId = +params['propertyAreaId'];
      this.selectedPropertyId = +params['propertyId'];
      this.getProperty(this.selectedPropertyId);
      this.areaRulesStateService.setPropertyAreaId(this.propertyAreaId);
      this.getAreaRules();
      this.getArea(this.propertyAreaId);
    });
  }

  // getChemicals() {
  //   this.getChemicalsSub$ = this.chemicalsStateService
  //     .getAllChemicals(this.selectedPropertyId)
  //     .subscribe((data) => {
  //       if (data && data.success) {
  //         // map folder names to items
  //         if (data.model.total > 0) {
  //           this.chemicalsModel = {
  //             ...data.model,
  //             entities: data.model.entities.map((x) => {
  //               return {
  //                 ...x,
  //               };
  //             }),
  //           };
  //         } else {
  //           this.chemicalsModel = data.model;
  //         }
  //         // Required if page or anything else was changed
  //       }
  //     });
  // }

  getArea(propertyAreaId: number) {
    this.getAreaSub$ = this.areasService
      .getAreaByPropertyAreaId(propertyAreaId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.selectedArea = operation.model;
          this.breadcrumbs[2] = {name: this.selectedArea.name};
          // this.getChemicals();
        }
      });
  }

  getAreaRules() {
    this.getAreaRulesSub$ = this.areaRulesStateService
      .getAllAreaRules()
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
          const modal = this.dialog.open(AreaRulePlanModalComponent,
            {
              ...dialogConfigHelper(this.overlay,
                {
                  areaRule: rule,
                  propertyId: this.selectedPropertyId,
                  area: this.selectedArea,
                  areaRulePlan: operation.model,
                }),
              minWidth: 500,
            });
          this.updateAreaRulePlanSub$ = modal.componentInstance.updateAreaRulePlan
            .subscribe(x => this.onUpdateAreaRulePlan(x, modal));
        });
    } else {
      const modal = this.dialog.open(AreaRulePlanModalComponent,
        {
          ...dialogConfigHelper(this.overlay,
            {
              areaRule: rule,
              propertyId: this.selectedPropertyId,
              area: this.selectedArea,
            }),
          minWidth: 500,
        });
      this.updateAreaRulePlanSub$ = modal.componentInstance.updateAreaRulePlan
        .subscribe(x => this.onUpdateAreaRulePlan(x, modal));
    }
  }

  showEditAreaRuleModal(rule: AreaRuleSimpleModel) {
    this.getSingleAreaRuleSub$ = this.areasService
      .getSingleAreaRule(rule.id, this.selectedPropertyId)
      .subscribe((operation) => {
        if (operation && operation.success) {
          const modal = this.dialog.open(AreaRuleEditModalComponent,
            {...dialogConfigHelper(this.overlay,
                {areaRule: operation.model, selectedArea: this.selectedArea, planningStatus: rule.planningStatus}),
              minWidth: this.selectedArea.type === 10 ? 800 : 500,
            }
          );
          this.onUpdateAreaRuleSub$ = modal.componentInstance.updateAreaRule.subscribe(x => this.onUpdateAreaRule(x, modal));
        }
      });
  }

  showAreaRuleCreateModal() {
    const modal = this.dialog.open(AreaRuleCreateModalComponent,
      {...dialogConfigHelper(this.overlay,
          {selectedArea: this.selectedArea, areaRules: this.areaRules}),
        minWidth: this.selectedArea.type === 10 ? 800 : 500,
      }
    );
    this.onCreateAreaRuleSub$ = modal.componentInstance.createAreaRule.subscribe(x => this.onCreateAreaRule(x, modal));
    this.onDeleteAreaRuleFromModalSub$ = modal.componentInstance.deleteAreaRule.subscribe(x => this.onDeleteAreaRules(x, modal));
  }

  showDeleteAreaRuleModal(rule: AreaRuleSimpleModel) {
    const modal = this.dialog.open(AreaRuleDeleteModalComponent, {...dialogConfigHelper(this.overlay, rule)});
    this.onDeleteAreaRuleSub$ = modal.componentInstance.deleteAreaRule.subscribe(x => this.onDeleteAreaRule(x, modal))
  }

  onCreateAreaRule(model: AreaRulesCreateModel, modal: MatDialogRef<AreaRuleCreateModalComponent>) {
    this.createAreaRuleSub$ = this.areasService
      .createAreaRules({...model, propertyAreaId: this.propertyAreaId})
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          modal.close();
        }
      });
  }

  onUpdateAreaRule(model: AreaRuleUpdateModel, modal: MatDialogRef<AreaRuleEditModalComponent>) {
    this.editAreaRuleSub$ = this.areasService
      .updateAreaRule(model)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          modal.close();
        }
      });
  }

  onDeleteAreaRule(areaRuleId: number, modal: MatDialogRef<AreaRuleDeleteModalComponent>) {
    this.deleteAreaRuleSub$ = this.areasService
      .deleteAreaRule(areaRuleId)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          modal.close();
        }
      });
  }

  onUpdateAreaRulePlan(rulePlanning: AreaRulePlanningModel, modal: MatDialogRef<AreaRulePlanModalComponent>) {
    this.planAreaRuleSub$ = this.areasService
      .updateAreaRulePlanning(rulePlanning)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          modal.close();
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

  onDeleteAreaRules(areaRuleIds: number[], modal: MatDialogRef<AreaRuleCreateModalComponent>) {
    this.deleteAreaRulesSub$ = this.areasService
      .deleteAreaRules(areaRuleIds)
      .subscribe((data) => {
        if (data && data.success) {
          this.getAreaRules();
          modal.close();
        }
      });
  }

  ngOnDestroy(): void {
  }

  updateEntityList(model: Array<EntityItemModel>, modal: MatDialogRef<AreaRuleEntityListModalComponent>) {
    if (!this.selectedArea.groupId) {
      this.backendConfigurationPnPropertiesService.createEntityList(model, this.propertyAreaId)
        .subscribe((x => {
          if (x.success) {
            modal.close();
            this.getArea(this.propertyAreaId);
          }
        }));
    } else {
      this.entitySelectService.getEntitySelectableGroup(this.selectedArea.groupId)
        .subscribe(data => {
          if (data.success) {
            this.entitySelectService.updateEntitySelectableGroup({
              entityItemModels: model,
              groupUid: +data.model.microtingUUID,
              ...data.model
            }).subscribe(x => {
              if (x.success) {
                modal.close();
              }
            });
          }
        });
    }
  }

  showEntityListEditModal($event: number) {
    const modal = this.dialog
      .open(AreaRuleEntityListModalComponent, {...dialogConfigHelper(this.overlay, $event)});
    this.propertyUpdateSub$ = modal.componentInstance.entityListChanged.subscribe(x => this.updateEntityList(x, modal));
  }

  sortTable(sort: Sort) {
    this.areaRulesStateService.onSortTable(sort.active);
    this.getAreaRules();
  }
}
