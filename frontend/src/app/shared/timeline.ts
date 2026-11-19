import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { Activity } from '../core/models';

@Component({
  selector: 'app-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline">
      @for (a of items(); track $index) {
        <li>
          <div class="tl-meta">{{ a.at | date: 'd MMM y, HH:mm' }} · {{ a.actor }}</div>
          <div>{{ a.detail }}</div>
        </li>
      } @empty {
        <li class="muted">No activity yet.</li>
      }
    </ol>
  `,
})
export class Timeline {
  items = input.required<Activity[]>();
}
