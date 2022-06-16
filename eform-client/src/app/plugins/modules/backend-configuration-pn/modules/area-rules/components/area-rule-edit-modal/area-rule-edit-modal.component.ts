import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { debounceTime, switchMap } from 'rxjs/operators';
import { TemplateListModel, TemplateRequestModel } from 'src/app/common/models';
import { EFormService } from 'src/app/common/services';
import {
  AreaModel, AreaRuleCreateModel,
  AreaRuleModel,
  AreaRuleUpdateModel,
} from '../../../../models';
import * as R from 'ramda';

@Component({
  selector: 'app-area-rule-edit-modal',
  templateUrl: './area-rule-edit-modal.component.html',
  styleUrls: ['./area-rule-edit-modal.component.scss'],
})
export class AreaRuleEditModalComponent implements OnInit {
  @Input() selectedArea: AreaModel = new AreaModel();
  @ViewChild('frame', { static: false }) frame;
  @Output()
  updateAreaRule: EventEmitter<AreaRuleUpdateModel> = new EventEmitter<AreaRuleUpdateModel>();
  selectedAreaRule: AreaRuleUpdateModel = new AreaRuleUpdateModel();
  typeahead = new EventEmitter<string>();
  templatesModel: TemplateListModel = new TemplateListModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
  days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  hours = ['00', '01', '02', '03' ,'04' ,'05', '06', '07', '08', '09', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20', '21', '22', '23'];

  constructor(
    private eFormService: EFormService,
    private cd: ChangeDetectorRef
  ) {
    this.typeahead
      .pipe(
        debounceTime(200),
        switchMap((term) => {
          this.templateRequestModel.nameFilter = term;
          return this.eFormService.getAll(this.templateRequestModel);
        })
      )
      .subscribe((items) => {
        this.templatesModel = items.model;
        this.cd.markForCheck();
      });
  }

  ngOnInit() {}

  show(model: AreaRuleModel) {
    // @ts-ignore
    this.selectedAreaRule = R.clone(model);
    this.frame.show();
  }

  hide() {
    this.selectedAreaRule = new AreaRuleUpdateModel();
    this.frame.hide();
  }

  onUpdateAreaRule() {
    this.updateAreaRule.emit(this.selectedAreaRule);
  }

  changeEform(eformId: number) {
    this.selectedAreaRule.eformId = eformId;
    this.selectedAreaRule.eformName = this.templatesModel.templates.find(
      (x) => x.id === eformId
    ).label;
  }

  checked($event: any, i: number, j: number, areaRule: AreaRuleCreateModel) {
    for (let k = 0; k < areaRule.typeSpecificFields.poolHoursModel.parrings.length; k++) {
      if (areaRule.typeSpecificFields.poolHoursModel.parrings[k].dayOfWeek === i && areaRule.typeSpecificFields.poolHoursModel.parrings[k].index === j) {
        areaRule.typeSpecificFields.poolHoursModel.parrings[k].isActive = $event.target.checked;
      }
    }
  }

  isChecked(i: number, j: number, areaRule: AreaRuleCreateModel) {
    for (let k = 0; k < areaRule.typeSpecificFields.poolHoursModel.parrings.length; k++) {
      if (areaRule.typeSpecificFields.poolHoursModel.parrings[k].dayOfWeek === i && areaRule.typeSpecificFields.poolHoursModel.parrings[k].index === j) {
        return areaRule.typeSpecificFields.poolHoursModel.parrings[k].isActive;
      }
    }
  }
}
