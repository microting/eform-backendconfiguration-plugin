import {Component, EventEmitter, Input, OnInit, Output, ViewChild} from '@angular/core';
import {SiteDto} from 'src/app/common/models/dto';
import {DeviceUserService} from 'src/app/common/services/device-users';

@Component({
  selector: 'app-property-worker-delete-modal',
  templateUrl: './property-worker-delete-modal.component.html',
  styleUrls: ['./property-worker-delete-modal.component.scss']
})
export class PropertyWorkerDeleteModalComponent implements OnInit {
  @Input() selectedDeviceUser: SiteDto = new SiteDto();
  @Output() onUserDeleted: EventEmitter<void> = new EventEmitter<void>();
  @ViewChild('frame', { static: true }) frame;

  constructor(private deviceUserService: DeviceUserService) { }

  ngOnInit() {
  }

  show() {
    this.frame.show();
  }

  deleteSingle() {
    this.deviceUserService.deleteSingleDeviceUser(this.selectedDeviceUser.siteUid).subscribe(operation => {
      if (operation && operation.success) {
        this.onUserDeleted.emit();
        this.frame.hide();
      }
    });
  }
}
