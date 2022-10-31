import {Component, EventEmitter, OnDestroy, OnInit, Output, ViewChild} from '@angular/core';
import { CommonDictionaryModel } from 'src/app/common/models';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService,
} from '../../../../../services';
import { SitesService, TemplateFilesService } from 'src/app/common/services';
import {WorkOrderCaseCreateModel, WorkOrderCaseForReadModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { Subscription} from 'rxjs';
import { ModalDirective } from 'angular-bootstrap-md';
import * as R from 'ramda';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Gallery, GalleryItem, ImageItem } from '@ngx-gallery/core';
import { Lightbox } from '@ngx-gallery/lightbox';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-create-show-modal',
  templateUrl: './task-management-create-show-modal.component.html',
  styleUrls: ['./task-management-create-show-modal.component.scss'],
})
export class TaskManagementCreateShowModalComponent
  implements OnInit, OnDestroy
{
  @ViewChild('frame', { static: false }) frame;
  @Output() taskCreated: EventEmitter<void> = new EventEmitter<void>();
  @ViewChild('addNewImageModal', { static: false })
  addNewImageModal: ModalDirective;
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

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
    private imageService: TemplateFilesService,
    public gallery: Gallery,
    public lightbox: Lightbox,
    private taskManagementService: BackendConfigurationPnTaskManagementService
  ) {}

  ngOnInit(): void {
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
    });
  }

  show(workOrderCase?: WorkOrderCaseForReadModel) {
    this.getProperties();
    if (workOrderCase) {
      this.getPropertyAreas(workOrderCase.propertyId);
      this.getSites(workOrderCase.propertyId);
      this.workOrderCaseForm.patchValue(
        {
          propertyId: workOrderCase.propertyId,
          areaName: workOrderCase.areaName,
          assignedTo: workOrderCase.assignedSiteId,
          descriptionTask: workOrderCase.description,
        },
        { emitEvent: false }
      );
      this.workOrderCaseForm.disable({ emitEvent: false });
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
            this.workOrderCaseForm.patchValue(
              {
                areaName: null,
                assignedTo: null,
              }
            );
          }
        });
      this.isCreate = true;
    }
    this.frame.show();
  }

  hide() {
    this.images = [];
    this.propertyAreas = [];
    this.assignedSitesToProperty = [];
    if (this.propertyIdValueChangesSub$) {
      this.propertyIdValueChangesSub$.unsubscribe();
    }
    this.frame.hide();
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
      sort: 'Id',
      isSortDsc: false,
      pageSize: 100000,
      offset: 0,
      pageIndex: 0
    }).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [...data.model.entities.filter((x) => x.workorderEnable)
          .map((x) => {
            return {name: `${x.cvr ? x.cvr : ''} - ${x.chr ? x.chr : ''} - ${x.name}`, description: '', id: x.id};
          })];
      }
    });
  }

  // getProperties() {
  //   this.propertyService.getAllPropertiesDictionary().subscribe((data) => {
  //     if (data && data.success && data.model) {
  //       this.properties = data.model;
  //     }
  //   });
  // }

  getSites(propertyId: number) {
    this.sitesService.getAllSitesDictionary().subscribe((result) => {
      if (result && result.success && result.success) {
        const sites = result.model;
        this.propertyService.getPropertiesAssignments().subscribe((data) => {
          if (data && data.success && data.model) {
            data.model.forEach(
              (x) =>
                (x.assignments = x.assignments.filter(
                  (x) => x.isChecked && x.propertyId === propertyId
                ))
            );
            data.model = data.model.filter((x) => x.assignments.length > 0);
            this.assignedSitesToProperty = data.model.map((x) => {
              return (sites.find((y) => y.id === x.siteId) !== undefined) ?
              {
                id: x.siteId,
                name: sites.find((y) => y.id === x.siteId).name,
                description: '',
              } : null;
            });
            this.assignedSitesToProperty = this.assignedSitesToProperty.filter((x) => x !== null);
          }
        });
      }
    });
  }

  addPicture() {
    const src = URL.createObjectURL(this.newImage);
    this.images.push({
      src: src,
      thumbnail: src,
      fileName: this.newImage.name,
      file: this.newImage,
    });
    this.addNewImageModal.hide();
    this.newImage = null;
    this.frame.show();
  }

  onFileSelected(event: Event) {
    // @ts-ignore
    this.newImage = R.last(event.target.files);
  }

  ngOnDestroy(): void {
    this.imageSubs$.forEach((x) => x.unsubscribe());
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
    const workOrderCase: WorkOrderCaseCreateModel = {
      assignedSiteId: rawValue.assignedTo,
      areaName: rawValue.areaName,
      propertyId: rawValue.propertyId,
      description: rawValue.descriptionTask,
      files: this.images.map(x => x.file),
    }
    this.taskManagementService.createWorkOrderCase(workOrderCase)
      .subscribe(data => {
        if (data && data.success) {
          this.taskCreated.emit();
          this.hide();
        }
    })
  }
}
