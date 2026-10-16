import {Component, EventEmitter, Input, Output} from '@angular/core';
import {ArchiveFolderOption} from '../../../../helpers';

/**
 * The 2-level folder picker: both levels, labelled by path ("Drift › Fakturaer") so two subfolders with
 * the same name can be told apart, searchable by path, locked folders show who can read them.
 */
@Component({
  selector: 'app-archive-folder-picker',
  templateUrl: './archive-folder-picker.component.html',
  standalone: false,
})
export class ArchiveFolderPickerComponent {
  @Input() options: ArchiveFolderOption[] = [];
  @Input() value: number | null = null;
  @Input() pickerId = 'archiveFolderSelect';
  /** Translation key of the field label. */
  @Input() label = 'Folder';
  @Input() disabled = false;
  @Output() valueChange = new EventEmitter<number | null>();
}
