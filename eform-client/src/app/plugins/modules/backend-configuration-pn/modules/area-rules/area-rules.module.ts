import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
  AreaRulePlanModalComponent,
  AreaRulePlanT1Component,
  AreaRulePlanT2Component,
  AreaRulePlanT3Component,
  AreaRulePlanT4Component,
  AreaRulePlanT5Component,
  AreaRulesContainerComponent,
  AreaRulesTableComponent,
} from './components';

@NgModule({
  declarations: [
    AreaRulesContainerComponent,
    AreaRulesTableComponent,
    AreaRuleCreateModalComponent,
    AreaRuleEditModalComponent,
    AreaRuleDeleteModalComponent,
    AreaRulePlanModalComponent,
    AreaRulePlanT1Component,
    AreaRulePlanT2Component,
    AreaRulePlanT3Component,
    AreaRulePlanT4Component,
    AreaRulePlanT5Component,
  ],
  imports: [CommonModule],
})
export class AreaRulesModule {}
