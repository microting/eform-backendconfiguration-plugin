import {Component, Inject, OnInit} from '@angular/core';
import {UnitsService} from 'src/app/common/services';
import {DeviceUserModel} from '../../../../models';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-property-worker-otp-modal',
  templateUrl: './property-worker-otp-modal.component.html',
  styleUrls: ['./property-worker-otp-modal.component.scss']
})
export class PropertyWorkerOtpModalComponent implements OnInit {
  constructor(
    private unitsService: UnitsService,
    public dialogRef: MatDialogRef<PropertyWorkerOtpModalComponent>,
    @Inject(MAT_DIALOG_DATA) public selectedSimpleSite: DeviceUserModel = new DeviceUserModel(),
  ) {}

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
