import {Component, OnDestroy, OnInit} from '@angular/core';
import {saveAs} from 'file-saver';
import {ToastrService} from 'ngx-toastr';
import {
  NewReportEformPnModel,
  ReportPnGenerateModel,
} from '../../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {BehaviorSubject, forkJoin, Observable, Subscription, asyncScheduler, of} from 'rxjs';
import {ActivatedRoute, Router} from '@angular/router';
import {parseISO} from 'date-fns';
import {SharedTagModel,} from 'src/app/common/models';
import {EmailRecipientsService, TemplateFilesService} from 'src/app/common/services';
import {AuthStateService} from 'src/app/common/store';
import {Gallery, GalleryItem, ImageItem} from '@ngx-gallery/core';
import {Lightbox} from '@ngx-gallery/lightbox';
import {ViewportScroller} from '@angular/common';
import {BackendConfigurationPnReportService} from '../../../../services';
import {catchError, tap} from 'rxjs/operators';

@AutoUnsubscribe()
@Component({
  selector: 'app-backend-configuration-pn-report',
  templateUrl: './report-container.component.html',
  styleUrls: ['./report-container.component.scss'],
})
export class ReportContainerComponent implements OnInit, OnDestroy {
  reportsModel: NewReportEformPnModel[] = [];
  range: Date[] = [];
  availableTags: SharedTagModel[] = [];
  currentUserFullName: string;
  images: { key: number, value: any }[] = [];
  galleryImages: GalleryItem[] = [];
  dateFrom: any;
  dateTo: any;
  startWithParams = false;
  private observableReportsModel = new BehaviorSubject<NewReportEformPnModel[]>([]);

  getTagsSub$: Subscription;
  generateReportSub$: Subscription;
  downloadReportSub$: Subscription;
  imageSub$: Subscription[] = [];

  constructor(
    private emailRecipientsService: EmailRecipientsService,
    private activateRoute: ActivatedRoute,
    private reportService: BackendConfigurationPnReportService,
    private toastrService: ToastrService,
    private router: Router,
    public authStateService: AuthStateService,
    public gallery: Gallery,
    public lightbox: Lightbox,
    private imageService: TemplateFilesService,
    private viewportScroller: ViewportScroller
  ) {
    this.activateRoute.params.subscribe((params) => {
      this.dateFrom = params['dateFrom'];
      this.dateTo = params['dateTo'];
      this.range.push(parseISO(params['dateFrom']));
      this.range.push(parseISO(params['dateTo']));
      this.startWithParams = !!(this.dateTo && this.dateFrom);
      // const model = {
      //   dateFrom: params['dateFrom'],
      //   dateTo: params['dateTo'],
      //   tagIds: planningsReportQuery.pageSetting.filters.tagIds,
      //   type: '',
      //   version2: true
      // };
      // if (model.dateFrom !== undefined) {
      //   this.onGenerateReport(model);
      // }
    });
    // this.observableReportsModel.subscribe(x => {
    //   if (x.length && this.startWithParams) {
    //     const task = _ => this.planningsReportQuery.selectScrollPosition$
    //       .subscribe(value => this.viewportScroller.scrollToPosition(value));
    //     asyncScheduler.schedule(task, 1000);
    //     this.startWithParams = false;
    //   }
    // });
  }

  ngOnInit() {
    this.getTags();
  }

  getTags() {
    this.getTagsSub$ = this.reportService.getPlanningsTags().subscribe((data) => {
      if (data && data.success) {
        this.availableTags = data.model;
      }
    });
  }

  onGenerateReport(model: ReportPnGenerateModel) {
    this.dateFrom = model.dateFrom;
    this.dateTo = model.dateTo;
    // this.generateReportSub$ = this.reportService
    //   .generateNewReport({
    //     dateFrom: model.dateFrom,
    //     dateTo: model.dateTo,
    //     tagIds: this.planningsReportQuery.pageSetting.filters.tagIds,
    //     type: '',
    //     version2: true
    //   })
    //   .subscribe((data) => {
    //     if (data && data.success) {
    //       this.reportsModel = data.model;
    //       this.observableReportsModel.next(data.model);
    //     }
    //   });
  }

  onDownloadReport(model: ReportPnGenerateModel) {
    model.version2 = true;
    this.downloadReportSub$ = this.reportService
      .downloadFileReport(model)
      .pipe(
        tap((data) => saveAs(data, `${model.dateFrom}_${model.dateTo}_report.${model.type}`)),
        catchError((_) => {
          this.toastrService.error('Error downloading report');
          return of(null);
        })
      )
      .subscribe();
  }

  onPlanningCaseDeleted() {
    const model = {
      dateFrom: this.dateFrom,
      dateTo: this.dateTo,
      tagIds: [],
      type: '',
      version2: true
    };
    if (model.dateFrom !== undefined) {
      this.onGenerateReport(model);
    }
  }

  getImages(/*reportEformPnModel: NewReportEformPnModel, */caseId: number) {
    this.images = [];
    const observables: Observable<any>[] = [];
    // Get an array of all image names associated with a specific caseId.
    const imageNamesByCaseId = this.reportsModel
      .map(x => x.groupEform) // Map over the reportsModel array to get an array of groupEform objects.
      .flat() // Flatten the array of groupEform objects into a single array.
      .map(x => x.imageNames) // Map over the flattened array of groupEform objects to get an array of imageNames objects.
      .flat() // Flatten the array of imageNames objects into a single array.
      .filter(x => x.caseId === caseId); // Filter the array of imageNames objects to include only those with a matching caseId.
    const length = imageNamesByCaseId.length;
    imageNamesByCaseId
      .forEach((imageValue) => {
        observables.push(this.imageService.getImage(imageValue.imageName));
        if (length === observables.length) {
          this.imageSub$.forEach(sub => sub.unsubscribe());
          this.imageSub$.push(forkJoin(observables).subscribe(blobArr => {
            if (length === blobArr.length) {
              blobArr.forEach((blob, index) => {
                const imageUrl = URL.createObjectURL(blob);
                const val = {
                  src: imageUrl,
                  thumbnail: imageUrl,
                  fileName: imageValue.imageName,
                  name: imageValue.label,
                  geoTag: imageValue.geoLink
                };
                this.images.push({key: Number(imageValue.caseId), value: val});
                this.images.sort((a, b) => a.key < b.key ? -1 : a.key > b.key ? 1 : 0);
                if (index + 1 === blobArr.length) {
                  this.updateGallery();
                  this.openPicture(0);
                }
              });
            }
          }));
        }
      });
  }

  updateGallery() {
    this.galleryImages = this.images.map(value => new ImageItem({src: value.value.src, thumb: value.value.thumbnail}));
  }

  openPicture(i: any) {
    if (this.galleryImages.length > 1) {
      this.gallery.ref('lightbox', {counterPosition: 'bottom', loadingMode: 'indeterminate'}).load(this.galleryImages);
      this.lightbox.open(i);
    } else {
      this.gallery.ref('lightbox', {counter: false, loadingMode: 'indeterminate'}).load(this.galleryImages);
      this.lightbox.open(i);
    }
  }

  onClickViewPicture(model: {/*reportIndex: number, */caseId: number }) {
    // const reportEformPnModel = this.reportsModel[model.reportIndex];
    this.getImages(/*reportEformPnModel, */model.caseId);
  }

  ngOnDestroy(): void {
    this.imageSub$.forEach(sub => sub.unsubscribe());
  }
}
