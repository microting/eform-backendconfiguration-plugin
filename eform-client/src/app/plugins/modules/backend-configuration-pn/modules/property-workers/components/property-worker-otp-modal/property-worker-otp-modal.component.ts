import {Component, OnInit, inject} from '@angular/core';
import {UnitsService} from 'src/app/common/services';
import {DeviceUserModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-property-worker-otp-modal',
    templateUrl: './property-worker-otp-modal.component.html',
    styleUrls: ['./property-worker-otp-modal.component.scss'],
    standalone: false
})
export class PropertyWorkerOtpModalComponent implements OnInit {
  private unitsService = inject(UnitsService);
  public dialogRef = inject(MatDialogRef<PropertyWorkerOtpModalComponent>);
  public selectedSimpleSite = inject<DeviceUserModel>(MAT_DIALOG_DATA);

  

  ngOnInit() {
  }

  hide(result = false) {
    this.dialogRef.close(result);
  }

  requestOtp() {
    this.unitsService.requestOtp(this.selectedSimpleSite.unitId).subscribe(operation => {
      if (operation && operation.success) {
        this.hide(true);
      }
    });
  }
}
