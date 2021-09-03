import {Component, EventEmitter, Input, OnInit, Output, ViewChild} from '@angular/core';
import {SiteDto} from 'src/app/common/models/dto';
import {UnitsService} from 'src/app/common/services/advanced';

@Component({
  selector: 'app-property-worker-otp-modal',
  templateUrl: './property-worker-otp-modal.component.html',
  styleUrls: ['./property-worker-otp-modal.component.scss']
})
export class PropertyWorkerOtpModalComponent implements OnInit {
  @Input() selectedSimpleSite: SiteDto = new SiteDto();
  @Output() onNewOtpRequested: EventEmitter<void> = new EventEmitter<void>();
  @ViewChild('frame', { static: true }) frame;

  constructor(private unitsService: UnitsService) { }

  ngOnInit() {
  }

  show() {
    this.frame.show();
  }

  requestOtp() {
    this.unitsService.requestOtp(this.selectedSimpleSite.unitId).subscribe(operation => {
      if (operation && operation.success) {
        this.frame.hide();
        this.onNewOtpRequested.emit();
      }
    });
  }
}
