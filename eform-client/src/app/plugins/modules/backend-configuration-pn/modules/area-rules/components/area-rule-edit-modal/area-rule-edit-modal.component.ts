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
  AreaModel,
  AreaRuleModel,
  AreaRuleUpdateModel,
} from '../../../../models';

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
    this.selectedAreaRule = { ...model };
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
}
