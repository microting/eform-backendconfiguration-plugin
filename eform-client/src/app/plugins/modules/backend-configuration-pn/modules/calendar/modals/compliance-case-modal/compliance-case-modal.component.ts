import {
  Component,
  Inject,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
  inject,
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
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
import {BackendConfigurationPnCompliancesService} from '../../../../services';
import {parseISO} from 'date-fns';
import * as R from 'ramda';

export interface ComplianceCaseModalData {
  sdkCaseId: number;
  templateId: number;
  propertyId: number;
  deadline: string;
  complianceId: number;
  workerId: number;
  /**
   * Optional event-start ISO string (Compliance.Deadline day +
   * CalendarConfiguration.StartHour, UTC). When supplied the modal pre-fills
   * the doneAt picker with this moment so Case.DoneAt records the scheduled
   * event-start rather than the deadline's midnight. Falls back to
   * `deadline` when absent.
   */
  eventStart?: string;
}

@Component({
  selector: 'app-compliance-case-modal',
  templateUrl: './compliance-case-modal.component.html',
  styleUrls: ['./compliance-case-modal.component.scss'],
  standalone: false,
})
export class ComplianceCaseModalComponent implements OnInit {
  private dialogRef = inject(MatDialogRef<ComplianceCaseModalComponent>);
  private backendConfigurationPnCompliancesService = inject(BackendConfigurationPnCompliancesService);
  private eFormService = inject(EFormService);

  @ViewChildren(CaseEditElementComponent)
  editElements: QueryList<CaseEditElementComponent>;
  @ViewChild('caseConfirmation', {static: false}) caseConfirmation;

  id: number;
  propertyId: number;
  eFormId: number;
  deadline: string;
  eventStart?: string;
  complianceId: number;
  workerId: number;
  currenteForm: TemplateDto = new TemplateDto();
  replyElement: ReplyElementDto = new ReplyElementDto();
  requestModels: Array<CaseEditRequest> = [];
  replyRequest: ReplyRequest = new ReplyRequest();
  maxDate: Date;
  isSaving = false;

  constructor(@Inject(MAT_DIALOG_DATA) public data: ComplianceCaseModalData) {
    this.id = data.sdkCaseId;
    this.propertyId = data.propertyId;
    this.eFormId = data.templateId;
    this.deadline = data.deadline;
    this.eventStart = data.eventStart;
    this.complianceId = data.complianceId;
    this.workerId = data.workerId;
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
          // Default the doneAt picker to the calendar event start
          // (Compliance.Deadline + CalendarConfiguration.StartHour) so the
          // submitted Case.DoneAt / DoneAtUserModifiable reflect the scheduled
          // moment rather than midnight. Fall back to `deadline` (midnight UTC)
          // when the backend didn't supply an eventStart — older response
          // shapes or non-compliance flows.
          this.replyElement.doneAt = parseISO(this.eventStart ?? this.deadline);
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
    this.isSaving = true;
    this.backendConfigurationPnCompliancesService
      .updateCase(this.replyRequest, this.currenteForm.id)
      .subscribe({
        next: (operation) => {
          this.isSaving = false;
          if (operation && operation.success) {
            this.dialogRef.close({saved: true});
          }
        },
        error: () => {
          this.isSaving = false;
        },
      });
  }

  cancel() {
    this.dialogRef.close({saved: false});
  }

  goToSection(location: string): void {
    setTimeout(() => {
      const target = document.querySelector(location) as HTMLElement | null;
      target?.parentElement?.scrollIntoView({behavior: 'smooth'});
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
