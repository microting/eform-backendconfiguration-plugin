import { MY_MOMENT_FORMATS } from 'src/app/common/helpers';

export const MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN = {
  parseInput: MY_MOMENT_FORMATS.parseInput, // 'l LT',
  fullPickerInput: MY_MOMENT_FORMATS.fullPickerInput, // 'YYYY/MM/DD',
  datePickerInput: MY_MOMENT_FORMATS.datePickerInput, // 'YYYY/MM/DD',
  timePickerInput: MY_MOMENT_FORMATS.timePickerInput, // 'YYYY/MM/DD',
  monthYearLabel: MY_MOMENT_FORMATS.monthYearLabel, // 'MMM YYYY',
  dateA11yLabel: MY_MOMENT_FORMATS.dateA11yLabel, // 'LL',
  monthYearA11yLabel: MY_MOMENT_FORMATS.monthYearA11yLabel, // 'MMMM YYYY',
};
