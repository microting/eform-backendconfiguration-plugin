import {
  Component,
  inject,
  OnInit,
} from '@angular/core';
import {DeviceUserService} from 'src/app/common/services';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {DeviceUserModel} from '../../../../models';

@Component({
    selector: 'app-property-worker-delete-modal',
    templateUrl: './property-worker-delete-modal.component.html',
    styleUrls: ['./property-worker-delete-modal.component.scss'],
    standalone: false
})
export class PropertyWorkerDeleteModalComponent implements OnInit {
  private deviceUserService = inject(DeviceUserService);
  private backendConfigurationPnPropertiesService = inject(BackendConfigurationPnPropertiesService);
  public dialogRef = inject(MatDialogRef<PropertyWorkerDeleteModalComponent>);
  public selectedDeviceUser = inject<DeviceUserModel>(MAT_DIALOG_DATA);

  ngOnInit() {
  }

  hide(result = false) {
    this.dialogRef.close(result);
  }

  deleteSingle() {
    this.deviceUserService
      .deleteSingleDeviceUser(this.selectedDeviceUser.siteUid) // remove user from app
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.backendConfigurationPnPropertiesService
            .removeWorkerAssignments(this.selectedDeviceUser.siteId) // remove user from plugin
            .subscribe((data) => {
              if (data && data.success) {
                this.hide(true);
              }
            });
        }
      });
  }
}
