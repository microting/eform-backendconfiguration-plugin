import {Component, EventEmitter, Inject, OnDestroy, OnInit, Output,} from '@angular/core';
import { CommonDictionaryModel } from 'src/app/common/models';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService,
} from '../../../../../services';
import { SitesService, TemplateFilesService } from 'src/app/common/services';
import {
  WorkOrderCaseCreateModel,
  WorkOrderCaseForReadModel, WorkOrderCaseUpdateModel,
} from '../../../../../models';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { Subscription} from 'rxjs';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Gallery, GalleryItem, ImageItem } from '@ngx-gallery/core';
import { Lightbox } from '@ngx-gallery/lightbox';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {AddPictureDialogComponent} from 'src/app/common/modules/eform-cases/components';
import {Overlay} from '@angular/cdk/overlay';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-create-show-modal',
  templateUrl: './task-management-create-show-modal.component.html',
  styleUrls: ['./task-management-create-show-modal.component.scss'],
})
export class TaskManagementCreateShowModalComponent
  implements OnInit, OnDestroy {
  @Output() taskCreated: EventEmitter<void> = new EventEmitter<void>();
  propertyAreas: string[] = [];
  properties: CommonDictionaryModel[] = [];
  assignedSitesToProperty: CommonDictionaryModel[] = [];
  description = '';
  isCreate = true;
  workOrderCaseForm: FormGroup;
  images: any[] = [];
  newImage: File;
  galleryImages: GalleryItem[] = [];

  propertyIdValueChangesSub$: Subscription;
  imageSubs$: Subscription[] = [];
  addPictureDialogComponentAddedPictureSub$: Subscription;
  getAllSitesDictionarySub$: Subscription;
  getPropertiesAssignmentsSub$: Subscription;
  currentWorkOrderCase: WorkOrderCaseForReadModel;

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
    private imageService: TemplateFilesService,
    public gallery: Gallery,
    public lightbox: Lightbox,
    public dialog: MatDialog,
    private overlay: Overlay,
    private taskManagementService: BackendConfigurationPnTaskManagementService,
    public dialogRef: MatDialogRef<TaskManagementCreateShowModalComponent>,
    @Inject(MAT_DIALOG_DATA) workOrderCase?: WorkOrderCaseForReadModel,
  ) {
    this.workOrderCaseForm = new FormGroup({
      propertyId: new FormControl({
        value: null,
        disabled: false,
      }, Validators.required),
      areaName: new FormControl({
        value: null,
        disabled: false,
      }),
      assignedTo: new FormControl({
        value: null,
        disabled: false,
      }, Validators.required),
      descriptionTask: new FormControl({
        value: null,
        disabled: false,
      }, Validators.required),
      priority: new FormControl({
        value: 3,
        disabled: false,
      }, Validators.required),
      caseStatusEnum: new FormControl({
        value: 1,
        disabled: false,
      }, Validators.required),
    });
    this.getProperties();
    if (workOrderCase) {
      this.currentWorkOrderCase = workOrderCase;
      this.getPropertyAreas(workOrderCase.propertyId);
      this.getSites(workOrderCase.propertyId);
      this.workOrderCaseForm.patchValue(
        {
          propertyId: workOrderCase.propertyId,
          areaName: workOrderCase.areaName,
          // assignedTo: workOrderCase.assignedSiteId,
          descriptionTask: workOrderCase.description,
          priority: workOrderCase.priority,
          caseStatusEnum: workOrderCase.caseStatusEnum,
        },
        { emitEvent: false }
      );
      // this.workOrderCaseForm.disable({ emitEvent: false });
      this.workOrderCaseForm.controls['propertyId'].disable({ emitEvent: false });
      this.workOrderCaseForm.controls['areaName'].disable({ emitEvent: false });
      this.description = workOrderCase.description;
      workOrderCase.pictureNames.forEach((fileName) => {
        this.imageSubs$.push(
          this.imageService.getImage(fileName).subscribe((blob) => {
            const imageUrl = URL.createObjectURL(blob);
            this.images.push({
              src: imageUrl,
              thumbnail: imageUrl,
              fileName: fileName,
            });
            this.images = this.images.sort((a, b) =>
              a.fileName.localeCompare(b.fileName)
            );
          })
        );
      });
      this.isCreate = false;
    } else {
      this.workOrderCaseForm.patchValue(
        {
          propertyId: null,
          areaName: null,
          assignedTo: null,
          descriptionTask: null,
        },
        { emitEvent: false }
      );
      this.workOrderCaseForm.enable({ emitEvent: false });
      this.description = '';
      this.propertyIdValueChangesSub$ = this.workOrderCaseForm
        .get('propertyId')
        .valueChanges.subscribe((propertyId) => {
          if (propertyId) {
            this.getPropertyAreas(propertyId);
            this.getSites(propertyId);
            // this.workOrderCaseForm.patchValue({areaName: null, assignedTo: null,});
          }
        });
      this.isCreate = true;
    }
  }

  ngOnInit(): void {
  }

  hide() {
    this.images = [];
    this.propertyAreas = [];
    this.assignedSitesToProperty = [];
    if (this.propertyIdValueChangesSub$) {
      this.propertyIdValueChangesSub$.unsubscribe();
    }
    this.dialogRef.close();
  }

  getPropertyAreas(propertyId: number) {
    // get entity items
    this.taskManagementService
      .getEntityItemsListByPropertyId(propertyId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.propertyAreas = data.model;
        }
      });
  }

  getProperties() {
    this.propertyService.getAllProperties({
      nameFilter: '',
      sort: 'Name',
      isSortDsc: false,
      pageSize: 100000,
      offset: 0,
      pageIndex: 0
    }).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [...data.model.entities.filter((x) => x.workorderEnable)
          .map((x) => {
            return {name: `${x.name}`, description: '', id: x.id};
          })];
      }
    });
  }

  getSites(propertyId: number) {
    this.getAllSitesDictionarySub$ = this.sitesService.getAllSitesDictionary().subscribe(result => {
      if (result && result.success && result.success) {
        const sites = result.model;
        this.getPropertiesAssignmentsSub$ = this.propertyService.getPropertiesAssignments().subscribe(data => {
          if (data && data.success && data.model) {
            data.model.forEach(x => x.assignments = x.assignments.filter(y => y.isChecked && y.propertyId === propertyId));
            data.model = data.model.filter((x) => x.assignments.length > 0 && x.taskManagementEnabled);
            this.assignedSitesToProperty = data.model.map(x => {
              const site = sites.find((y) => y.id === x.siteId)
              return {id: x.siteId, name: site ? site.name : '', description: '',};
            });
            this.assignedSitesToProperty = this.assignedSitesToProperty.filter((x) => x !== null).sort((a, b) => {
              return a.name.localeCompare(b.name);
            });
            if (this.currentWorkOrderCase) {
              this.workOrderCaseForm.patchValue(
                {
                  assignedTo: this.currentWorkOrderCase.assignedSiteId,
                },
                { emitEvent: false }
              );
            } else {
              this.workOrderCaseForm.patchValue({areaName: null, assignedTo: null,});
            }
          }
        });
      }
    });
  }

  addPicture(image: File, modal: MatDialogRef<AddPictureDialogComponent>) {
    const src = URL.createObjectURL(image);
    this.images = [...this.images, {
      src: src,
      thumbnail: src,
      fileName: image.name,
      file: image,
    }];
    modal.close();
  }

  openPicture(i: any) {
    // if (!this.isCreate) {
    this.updateGallery();
    if (this.galleryImages.length > 1) {
      this.gallery
        .ref('lightbox', {
          counterPosition: 'bottom',
          loadingMode: 'indeterminate',
        })
        .load(this.galleryImages);
      this.lightbox.open(i);
    } else {
      // this.gallery.destroyAll();
      // this.gallery.resetAll();
      this.gallery
        .ref('lightbox', { counter: false, loadingMode: 'indeterminate' })
        .load(this.galleryImages);
      this.lightbox.open(i);
    }
    // }
  }

  updateGallery() {
    this.galleryImages = [];
    this.images = this.images.sort((a, b) =>
      a.fileName.localeCompare(b.fileName)
    );
    this.images.forEach((value) => {
      this.galleryImages.push(
        new ImageItem({ src: value.src, thumb: value.thumbnail })
      );
    });
  }

  deleteImageByName(name) {
    this.images = this.images.filter((i) => i.fileName !== name);
  }

  create() {
    const rawValue = this.workOrderCaseForm.getRawValue();
    if (this.currentWorkOrderCase !== undefined) {

      const workOrderCase: WorkOrderCaseUpdateModel = {
        assignedSiteId: rawValue.assignedTo,
        areaName: rawValue.areaName,
        propertyId: rawValue.propertyId,
        description: rawValue.descriptionTask,
        files: this.images.map(x => x.file),
        id: this.currentWorkOrderCase.id,
        priority: rawValue.priority,
        caseStatusEnum: rawValue.caseStatusEnum,
      };
      this.taskManagementService.updateWorkOrderCase(workOrderCase)
        .subscribe(data => {
          if (data && data.success) {
            this.taskCreated.emit();
            this.hide();
          }
        });
    } else {
      const workOrderCase: WorkOrderCaseCreateModel = {
        assignedSiteId: rawValue.assignedTo,
        areaName: rawValue.areaName,
        propertyId: rawValue.propertyId,
        description: rawValue.descriptionTask,
        priority: rawValue.priority,
        files: this.images.map(x => x.file),
        caseStatusEnum: rawValue.caseStatusEnum,
      };
      this.taskManagementService.createWorkOrderCase(workOrderCase)
        .subscribe(data => {
          if (data && data.success) {
            this.taskCreated.emit();
            this.hide();
          }
        });
    }
  }

  openAddImage() {
    const modal = this.dialog.open(AddPictureDialogComponent, dialogConfigHelper(this.overlay));
    this.addPictureDialogComponentAddedPictureSub$ = modal.componentInstance.addedPicture.subscribe(x => this.addPicture(x, modal));
  }

  ngOnDestroy(): void {
    this.imageSubs$.forEach((x) => x.unsubscribe());
  }
}
