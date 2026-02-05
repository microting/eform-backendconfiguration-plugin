import {
  Component,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
  inject
} from '@angular/core';
import {ActivatedRoute, Router} from '@angular/router';
import {EFormService, CasesService} from 'src/app/common/services';
import {
  TemplateDto,
  CaseEditRequest,
  ReplyElementDto,
  ReplyRequest, ElementDto, DataItemDto,
} from 'src/app/common/models';
import {CaseEditElementComponent} from 'src/app/common/modules/eform-cases/components';
import * as R from 'ramda';
import {
  BackendConfigurationPnCasesService
} from '../../../services';
import {Store} from '@ngrx/store';

@Component({
    selector: 'app-backend-configuration-case-page',
    templateUrl: './backend-configuration-case-page.component.html',
    styleUrls: ['./backend-configuration-case-page.component.scss'],
    standalone: false
})
export class BackendConfigurationCasePageComponent implements OnInit {
  private activateRoute = inject(ActivatedRoute);
  private casesService = inject(CasesService);
  private store = inject(Store);
  private eFormService = inject(EFormService);
  private router = inject(Router);
  private backendConfigurationPnCasesService = inject(BackendConfigurationPnCasesService);

  @ViewChildren(CaseEditElementComponent)
  editElements: QueryList<CaseEditElementComponent>;
  @ViewChild('caseConfirmation', { static: false }) caseConfirmation;
  id: number;
  planningId: number;
  eFormId: number;
  currentTemplate: TemplateDto = new TemplateDto();
  replyElement: ReplyElementDto = new ReplyElementDto();
  reverseRoute: string;
  requestModels: Array<CaseEditRequest> = [];
  replyRequest: ReplyRequest = new ReplyRequest();
  maxDate: Date;
  initialDate: Date;



  ngOnInit() {
    this.activateRoute.params.subscribe((params) => {
      this.id = +params['id'];
      this.planningId = +params['planningId'];
      this.eFormId = +params['templateId'];
    });
    this.activateRoute.queryParams.subscribe((queryParams) => {
      this.reverseRoute = queryParams['reverseRoute'];
    })

    this.loadTemplateInfo();
    this.maxDate = new Date();
  }

  loadCase() {
    if (!this.id || this.id === 0) {
      return;
    }
    this.casesService
      .getById(this.id, this.currentTemplate.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.replyElement = operation.model;
          this.initialDate = this.replyElement.doneAt;
        }
      });
  }

  loadTemplateInfo() {
    if (this.eFormId) {
      this.eFormService.getSingle(this.eFormId).subscribe((operation) => {
        if (operation && operation.success) {
          this.currentTemplate = operation.model;
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
    this.replyRequest.id = this.replyElement.id;
    this.replyRequest.label = this.replyElement.label;
    this.replyRequest.elementList = this.requestModels;
    if (this.initialDate !== this.replyElement.doneAt) {
      this.replyRequest.doneAt = new Date(Date.UTC(this.replyElement.doneAt.getFullYear(),
        this.replyElement.doneAt.getMonth(), this.replyElement.doneAt.getDate(), 0, 0, 0));
    } else {
      this.replyRequest.doneAt = this.replyElement.doneAt;
    }
    this.backendConfigurationPnCasesService
      .updateCase(this.replyRequest, this.currentTemplate.id)
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.replyElement = new ReplyElementDto();
          // this.router
          //   .navigate([this.reverseRoute])
          //   .then();

          this.router
            .navigate([this.reverseRoute], { queryParams: { highlightId: this.id } })
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
    this.casesService
      .getById(this.id, this.currentTemplate.id)
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
