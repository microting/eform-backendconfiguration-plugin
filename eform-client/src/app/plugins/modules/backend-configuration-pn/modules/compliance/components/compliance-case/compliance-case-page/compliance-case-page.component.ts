import {
  Component,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
  inject
} from '@angular/core';
import {ActivatedRoute, Router} from '@angular/router';
import {EFormService} from 'src/app/common/services';
import {
  TemplateDto,
  CaseEditRequest,
  ReplyElementDto,
  ReplyRequest,
  ElementDto,
  DataItemDto,
} from 'src/app/common/models';
import {CaseEditElementComponent} from 'src/app/common/modules/eform-cases/components';
import {BackendConfigurationPnCompliancesService} from '../../../../../services';
import {parseISO} from 'date-fns';
import * as R from 'ramda';
import {Store} from '@ngrx/store';

@Component({
    selector: 'app-installation-case-page',
    templateUrl: './compliance-case-page.component.html',
    styleUrls: ['./compliance-case-page.component.scss'],
    standalone: false
})
export class ComplianceCasePageComponent implements OnInit {
  private activateRoute = inject(ActivatedRoute);
  private backendConfigurationPnCompliancesService = inject(BackendConfigurationPnCompliancesService);
  private eFormService = inject(EFormService);
  private router = inject(Router);

  @ViewChildren(CaseEditElementComponent)
  editElements: QueryList<CaseEditElementComponent>;
  @ViewChild('caseConfirmation', {static: false}) caseConfirmation;
  id: number;
  propertyId: number;
  eFormId: number;
  deadline: string;
  thirtyDays: string;
  complianceId: number;
  workerId: number;
  currenteForm: TemplateDto = new TemplateDto();
  replyElement: ReplyElementDto = new ReplyElementDto();
  reverseRoute: string;
  requestModels: Array<CaseEditRequest> = [];
  replyRequest: ReplyRequest = new ReplyRequest();
  maxDate: Date;

  

  ngOnInit() {
    this.activateRoute.params.subscribe((params) => {
      this.id = +params['sdkCaseId'];
      this.propertyId = +params['propertyId'];
      this.eFormId = +params['templateId'];
      this.deadline = params['deadline'];
      this.thirtyDays = params['thirtyDays'];
      this.complianceId = +params['complianceId'];
      this.workerId = +params['siteId'];
    });
    activateRoute.queryParams.subscribe((params) => {
      this.reverseRoute = params['reverseRoute'];
    });

    this.loadTemplateInfo();
    this.maxDate = new Date();
  }

  loadCase() {
    if (!this.id || this.id === 0) {
      return;
    }
    this.backendConfigurationPnCompliancesService
      .getCase(this.id, this.currenteForm.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.replyElement = operation.model;
          this.replyElement.doneAt = parseISO(this.deadline);
        }
      });
  }

  loadTemplateInfo() {
    if (this.eFormId) {
      this.eFormService.getSingle(this.eFormId).subscribe((operation) => {
        if (operation && operation.success) {
          this.currenteForm = operation.model;
          this.loadCase();
        }
      });
    }
  }

  saveCase() {
    this.requestModels = [];
    this.editElements.forEach((x) => {
      x.extractData();
      this.requestModels.push(x.requestModel);
    });
    this.replyRequest.id = this.id;
    this.replyRequest.label = this.replyElement.label;
    this.replyRequest.elementList = this.requestModels;
    this.replyRequest.doneAt = this.replyElement.doneAt;
    this.replyRequest.extraId = this.complianceId;
    this.replyRequest.siteId = this.workerId;
    this.backendConfigurationPnCompliancesService
      .updateCase(this.replyRequest, this.currenteForm.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.replyElement = new ReplyElementDto();
          this.router.navigate([this.reverseRoute]).then();
        }
      });
  }

  goToSection(location: string): void {
    window.location.hash = location;
    setTimeout(() => {
      document.querySelector(location).parentElement.scrollIntoView();
    });
  }

  partialLoadCase() {
    if (!this.id || this.id === 0) {
      return;
    }
    this.backendConfigurationPnCompliancesService
      .getCase(this.id, this.currenteForm.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          const fn = (pathForLens: Array<number | string>) => {
            const lens = R.lensPath(pathForLens);
            let dataItem: (ElementDto | DataItemDto) = R.view(lens, operation.model);
            // @ts-ignore
            if (dataItem.elementList !== undefined || dataItem.dataItemList !== undefined) {
              dataItem = dataItem as ElementDto;
              // R.set(R.lensPath([...pathForLens, 'extraPictures']), dataItem.extraPictures, this.replyElement);
              if (dataItem.elementList) {
                for (let i = 0; i < dataItem.elementList.length; i++) {
                  fn([...pathForLens, 'elementList', i]);
                }
              }
              if (dataItem.dataItemList) {
                for (let i = 0; i < dataItem.dataItemList.length; i++) {
                  fn([...pathForLens, 'dataItemList', i]);
                }
              }
            } else { // @ts-ignore
              if (dataItem.fieldType !== undefined) {
                dataItem = dataItem as DataItemDto;
                if (dataItem.fieldType === 'FieldContainer') {
                  for (let i = 0; i < dataItem.dataItemList.length; i++) {
                    fn([...pathForLens, 'dataItemList', i]);
                  }
                }
                if (dataItem.fieldType === 'Picture') {
                  // let oldDataItem = R.view(lens, this.replyElement);
                  // oldDataItem = {...oldDataItem, ...dataItem};
                  this.replyElement = R.set(lens, dataItem, this.replyElement);
                }
              }
            }
          };
          for (let i = 0; i < operation.model.elementList.length; i++) {
            fn(['elementList', i]);
          }
        }
      });
  }
}
