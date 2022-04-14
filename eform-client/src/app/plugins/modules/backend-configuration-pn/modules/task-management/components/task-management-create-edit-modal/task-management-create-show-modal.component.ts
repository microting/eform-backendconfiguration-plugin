import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TaskManagementStateService} from '../store';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {SitesService, TemplateFilesService} from 'src/app/common/services';
import {
  PropertyAreaModel,
  WorkOrderCaseForReadModel,
} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {FormControl, FormGroup} from '@angular/forms';
import {Subscription} from 'rxjs';
import {ModalDirective} from 'angular-bootstrap-md';
import * as R from 'ramda';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Gallery, GalleryItem, ImageItem} from '@ngx-gallery/core';
import {Lightbox} from '@ngx-gallery/lightbox';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-management-create-show-modal',
  templateUrl: './task-management-create-show-modal.component.html',
  styleUrls: ['./task-management-create-show-modal.component.scss'],
})
export class TaskManagementCreateShowModalComponent implements OnInit, OnDestroy{
  @ViewChild('frame', { static: false }) frame;
  @ViewChild('addNewImageModal', {static: false}) addNewImageModal: ModalDirective;
  propertyAreas: PropertyAreaModel[] = [];
  properties: CommonDictionaryModel[] = [];
  assignedSitesToProperty: CommonDictionaryModel[] = [];
  isCreate = true;
  workOrderCaseForm: FormGroup;
  images: any[] = [];
  imageSubs$: Subscription[] = [];
  newImage: File;
  galleryImages: GalleryItem[] = [];

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    private sitesService: SitesService,
    private imageService: TemplateFilesService,
    public gallery: Gallery,
    public lightbox: Lightbox,) {
  }

  ngOnInit(): void {
    this.workOrderCaseForm = new FormGroup({
      propertyId: new FormControl({
        value: undefined,
        disabled: false,
      }),
      areaId: new FormControl({
        value: undefined,
        disabled: false,
      }),
      assignedTo: new FormControl({
        value: undefined,
        disabled: false,
      }),
      description: new FormControl({
        value: '',
        disabled: false,
      }),
    });
  }

  show(workOrderCase?: WorkOrderCaseForReadModel) {
    this.getProperties();
    if(workOrderCase){
      this.getPropertyAreas(workOrderCase.propertyId);
      this.getSites(workOrderCase.propertyId);
      this.workOrderCaseForm = new FormGroup({
        propertyId: new FormControl({
          value: workOrderCase.propertyId,
          disabled: true,
        }),
        areaId: new FormControl({
          value: workOrderCase.areaId,
          disabled: true,
        }),
        assignedTo: new FormControl({
          value: workOrderCase.assignedSiteId,
          disabled: true,
        }),
        description: new FormControl({
          value: workOrderCase.description,
          disabled: true,
        }),
      });
      workOrderCase.pictureNames.forEach(fileName => {
        this.imageSubs$.push(this.imageService.getImage(fileName).subscribe(blob => {
          const imageUrl = URL.createObjectURL(blob);
          this.images.push({
            src: imageUrl,
            thumbnail: imageUrl,
            fileName: fileName,
          });
          this.images = this.images.sort((a, b) => a.fileName.localeCompare(b.fileName));
        }));
      });
      // this.workOrderCase = workOrderCase;
      this.isCreate = false;
    } else {
      this.workOrderCaseForm = new FormGroup({
        propertyId: new FormControl({
          value: undefined,
          disabled: false,
        }),
        areaId: new FormControl({
          value: undefined,
          disabled: false,
        }),
        assignedTo: new FormControl({
          value: undefined,
          disabled: false,
        }),
        description: new FormControl({
          value: '',
          disabled: false,
        }),
      });
      this.workOrderCaseForm.get('propertyId')
        .valueChanges.subscribe(propertyId => {
          if(propertyId){
            this.getPropertyAreas(propertyId);
            this.getSites(propertyId);
          }
      })
      this.isCreate = true;
    }
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  getPropertyAreas(propertyId: number) {
    this.propertyService.getPropertyAreas(propertyId)
      .subscribe((data) => {
        if(data && data.success && data.model) {
          this.propertyAreas = data.model.filter(x => x.activated);
        }
      })
  }

  getProperties() {
    this.propertyService.getAllPropertiesDictionary()
      .subscribe(data => {
        if(data && data.success && data.model){
          this.properties = data.model;
        }
      })
  }

  getSites(propertyId: number) {
    this.sitesService.getAllSitesDictionary()
      .subscribe(result => {
        if(result && result.success && result.success){
          const sites = result.model;
          this.propertyService.getPropertiesAssignments()
            .subscribe(data => {
              if(data && data.success && data.model){
                data.model.forEach(x => x.assignments = x.assignments.filter(x => x.isChecked && x.propertyId === propertyId));
                data.model = data.model.filter(x => x.assignments.length > 0);
                this.assignedSitesToProperty = data.model.map((x) => {
                  return {
                    id: x.siteId,
                    name: sites.find(y => y.id === x.siteId).name,
                    description: '',
                  }});
              }
            })
        }
      })
  }

  addPicture() {
    const src = URL.createObjectURL(this.newImage);
    this.images.push({
      src: src,
      thumbnail: src,
      fileName: this.newImage.name,
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
    this.imageSubs$.forEach(x => x.unsubscribe());
  }

  openPicture(i: any) {
    if(!this.isCreate) {
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
    }
  }

  updateGallery() {
    this.galleryImages = [];
    this.images = this.images.sort((a, b) => a.fileName.localeCompare(b.fileName));
    this.images.forEach(value => {
      this.galleryImages.push(new ImageItem({src: value.src, thumb: value.thumbnail}));
    });
  }

  deleteImageByName(name) {
    this.images = this.images.filter(i => i.fileName !== name);
  }
}
