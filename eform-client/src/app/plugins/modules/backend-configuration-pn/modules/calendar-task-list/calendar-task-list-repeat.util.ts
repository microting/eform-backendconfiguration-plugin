import {TranslateService} from '@ngx-translate/core';
import {CalendarRepeatService} from '../calendar/services/calendar-repeat.service';
import {CalendarTaskModel} from '../../models/calendar';
import {getCurrentLocale} from '../calendar/services/calendar-locale.helper';

export function formatRepeatText(
  repeatService: CalendarRepeatService,
  translate: TranslateService,
  task: CalendarTaskModel,
): string {
  if (!task.repeatRule || task.repeatRule === 'none') {
    return translate.instant('Does not repeat');
  }
  const meta = repeatService.reconstructMetaFromTask(task as any);
  return meta ? repeatService.formatCustomRepeatLabel(meta, getCurrentLocale(translate)) : task.repeatRule;
}
