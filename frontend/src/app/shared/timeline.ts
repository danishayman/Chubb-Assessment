import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { Activity, ClaimStatus } from '../core/models';
import { STATUS_LABELS } from './format';

@Component({
  selector: 'app-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline">
      @for (a of items(); track $index) {
        <li>
          <div class="tl-meta">{{ a.at | date: 'd MMM y, HH:mm' }} · {{ a.actor }}</div>
          <div>{{ humanize(a.detail) }}</div>
        </li>
      } @empty {
        <li class="muted">No activity yet.</li>
      }
    </ol>
  `,
})
export class Timeline {
  items = input.required<Activity[]>();

  /** The API logs raw status names ("UnderReview → Approved"); show the friendly labels. */
  protected humanize(text: string): string {
    return text.replace(/\b(Submitted|UnderReview|AwaitingInformation|Approved|Rejected|Settled)\b/g,
      s => STATUS_LABELS[s as ClaimStatus]);
  }
}
