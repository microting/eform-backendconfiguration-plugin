import {
  Component,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { EFormService, CasesService } from 'src/app/common/services';
import {
  TemplateDto,
  CaseEditRequest,
  ReplyElementDto,
  ReplyRequest, ElementDto, DataItemDto,
} from 'src/app/common/models';
import { AuthStateService } from 'src/app/common/store';
import { CaseEditElementComponent } from 'src/app/common/modules/eform-cases/components';
import {
  ComplianceCaseModule
} from 'src/app/plugins/modules/backend-configuration-pn/modules/compliance/components/compliance-case/compliance-case.module';
import {BackendConfigurationPnCompliancesService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {parseISO} from 'date-fns';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import * as R from 'ramda';

@Component({
  selector: 'app-installation-case-page',
  templateUrl: './compliance-case-page.component.html',
  styleUrls: ['./compliance-case-page.component.scss'],
})
export class ComplianceCasePageComponent implements OnInit {
  @ViewChildren(CaseEditElementComponent)
  editElements: QueryList<CaseEditElementComponent>;
  @ViewChild('caseConfirmation', { static: false }) caseConfirmation;
  id: number;
  propertyId: number;
  eFormId: number;
  deadline: string;
  thirtyDays: string;
  complianceId: number;
  currenteForm: TemplateDto = new TemplateDto();
  replyElement: ReplyElementDto = new ReplyElementDto();
  reverseRoute: string;
  requestModels: Array<CaseEditRequest> = [];
  replyRequest: ReplyRequest = new ReplyRequest();
  maxDate: Date;

  get userClaims() {
    return this.authStateService.currentUserClaims;
  }

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private activateRoute: ActivatedRoute,
    private backendConfigurationPnCompliancesService: BackendConfigurationPnCompliancesService,
    private eFormService: EFormService,
    private router: Router,
    private authStateService: AuthStateService
  ) {
    this.activateRoute.params.subscribe((params) => {
      this.id = +params['sdkCaseId'];
      this.propertyId = +params['propertyId'];
      this.eFormId = +params['templateId'];
      this.deadline = params['deadline'];
      this.thirtyDays = params['thirtyDays'];
      this.complianceId = +params['complianceId'];
      dateTimeAdapter.setLocale(authStateService.currentUserLocale);
    });
  }

  ngOnInit() {
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

  saveCase(navigateToPosts?: boolean) {
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
    this.backendConfigurationPnCompliancesService
      .updateCase(this.replyRequest, this.currenteForm.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.replyElement = new ReplyElementDto();
          this.router
            .navigate([
              '/plugins/backend-configuration-pn/compliances/' +
                this.propertyId
            ])
            .then();
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
              if(dataItem.elementList) {
                for (let i = 0; i < dataItem.elementList.length; i++) {
                  fn([...pathForLens, 'elementList', i]);
                }
              }
              if(dataItem.dataItemList) {
                for (let i = 0; i < dataItem.dataItemList.length; i++) {
                  fn([...pathForLens, 'dataItemList', i]);
                }
              }
            } else { // @ts-ignore
              if(dataItem.fieldType !== undefined){
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
          }
          for (let i = 0; i < operation.model.elementList.length; i++){
            fn(['elementList', i]);
          }
        }
      });
  }
}
